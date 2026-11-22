/*
    The native side of Sundex.Engine.Audio: a flat C surface over miniaudio, with opaque handles
    and blittable arguments, so the managed side needs no struct layouts.

    miniaudio.h is miniaudio v0.11.26 (tag 0.11.26, commit 877ea4a7c2601c7b0f2a44e83dfac42e6470fea6),
    public domain or MIT-0, unmodified. Build with build/audio-native.sh.

    Threading: nothing here locks except the stream's spinlock. The managed side serialises
    creating and freeing buffers, voices and streams against each other; every setter and getter
    may run on any thread, as miniaudio's ma_sound calls may.
*/
#define MA_NO_DECODING
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#define MA_NO_RESOURCE_MANAGER
#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <stdlib.h>
#include <string.h>

#if defined(_WIN32)
#define SUNDEX_API __declspec(dllexport)
#else
#define SUNDEX_API __attribute__((visibility("default")))
#endif

typedef struct sundex_device
{
    ma_engine engine; /* first, so the engine pointer miniaudio hands to notifications is this device */
    ma_context context;
    volatile ma_uint32 suspended;
    char name[MA_MAX_DEVICE_NAME_LENGTH + 1];
} sundex_device;

typedef struct sundex_buffer sundex_buffer;

typedef struct sundex_voice
{
    ma_audio_buffer_ref ref;
    ma_sound sound;
    sundex_buffer* owner;
    struct sundex_voice* prev;
    struct sundex_voice* next;
    int once;
} sundex_voice;

struct sundex_buffer
{
    sundex_device* device;
    void* data;
    ma_format format;
    ma_uint32 channels;
    ma_uint32 sample_rate;
    ma_uint64 frames;
    sundex_voice* voices;
};

typedef struct sundex_stream
{
    ma_data_source_base ds; /* first: the data source callbacks get this pointer */
    ma_sound sound;
    float* ring;
    ma_uint64 capacity;
    ma_uint32 channels;
    ma_uint32 sample_rate;
    volatile ma_uint64 written; /* frames ever put; only Put moves it */
    volatile ma_uint64 read;    /* frames ever handed to the mixer; only the audio thread and Flush move it */
    ma_uint64 start;            /* read where the last flush left it */
    ma_int64 base;
    /*
        Taken by the audio thread for its read and by Flush and the position getter. Each holds it
        for a copy of one period at most, so the audio thread spins for microseconds, not a lock's
        worth of scheduling.
    */
    volatile ma_spinlock lock;
} sundex_stream;

static int sundex_contains(const char* haystack, const char* needle)
{
    size_t length = strlen(needle);
    for (; *haystack != '\0'; haystack++)
    {
        size_t i = 0;
        while (i < length && haystack[i] != '\0' &&
               (haystack[i] | 0x20) == (needle[i] | 0x20))
            i++;
        if (i == length) return 1;
    }
    return 0;
}

static void sundex_on_notification(const ma_device_notification* notification)
{
    sundex_device* device = (sundex_device*)notification->pDevice->pUserData;
    if (notification->type == ma_device_notification_type_unlocked)
        ma_atomic_exchange_32(&device->suspended, 0);
}

/* backend: 0 for the platform's default list, 1 for miniaudio's null backend (real time, no hardware). */
SUNDEX_API int sundex_device_open(int backend, const char* name, int sample_rate, sundex_device** out)
{
    sundex_device* device;
    ma_backend null_backend = ma_backend_null;
    ma_device_id id;
    ma_device_id* chosen = NULL;
    ma_engine_config config;
    ma_device_info info;
    ma_result result;

    *out = NULL;
    device = (sundex_device*)calloc(1, sizeof(sundex_device));
    if (device == NULL) return MA_OUT_OF_MEMORY;

    result = ma_context_init(backend == 1 ? &null_backend : NULL, backend == 1 ? 1 : 0, NULL, &device->context);
    if (result != MA_SUCCESS)
    {
        free(device);
        return result;
    }

    if (name != NULL && name[0] != '\0')
    {
        ma_device_info* playback;
        ma_uint32 count, i;
        result = ma_context_get_devices(&device->context, &playback, &count, NULL, NULL);
        if (result == MA_SUCCESS)
        {
            for (i = 0; i < count; i++)
            {
                if (sundex_contains(playback[i].name, name))
                {
                    id = playback[i].id;
                    chosen = &id;
                    break;
                }
            }
        }

        if (chosen == NULL)
        {
            ma_context_uninit(&device->context);
            free(device);
            return MA_DOES_NOT_EXIST;
        }
    }

    config = ma_engine_config_init();
    config.pContext = &device->context;
    config.pPlaybackDeviceID = chosen;
    config.channels = 2;
    config.sampleRate = (ma_uint32)sample_rate;
    config.notificationCallback = sundex_on_notification;
#if defined(__EMSCRIPTEN__)
    /* the page starts the AudioContext suspended until a gesture unlocks it */
    device->suspended = 1;
#endif

    result = ma_engine_init(&config, &device->engine);
    if (result != MA_SUCCESS)
    {
        ma_context_uninit(&device->context);
        free(device);
        return result;
    }

    if (ma_device_get_info(ma_engine_get_device(&device->engine), ma_device_type_playback, &info) == MA_SUCCESS)
        memcpy(device->name, info.name, sizeof(device->name) - 1);

    *out = device;
    return MA_SUCCESS;
}

SUNDEX_API void sundex_device_close(sundex_device* device)
{
    ma_engine_uninit(&device->engine);
    ma_context_uninit(&device->context);
    free(device);
}

SUNDEX_API const char* sundex_device_name(sundex_device* device)
{
    return device->name;
}

SUNDEX_API int sundex_device_sample_rate(sundex_device* device)
{
    return (int)ma_engine_get_sample_rate(&device->engine);
}

/*
    An estimate of what the device buffers past the mixer. Most backends keep their whole buffer of
    periods full. PulseAudio keeps its target length (tlength) filled, and miniaudio reports that
    as one period of a larger maxlength.
*/
SUNDEX_API double sundex_device_latency(sundex_device* device)
{
    ma_device* output = ma_engine_get_device(&device->engine);
    ma_uint32 periods;
    if (output == NULL || output->playback.internalSampleRate == 0) return 0;
    periods = output->pContext->backend == ma_backend_pulseaudio ? 1 : output->playback.internalPeriods;
    return (double)output->playback.internalPeriodSizeInFrames * periods / output->playback.internalSampleRate;
}

SUNDEX_API int sundex_device_suspended(sundex_device* device)
{
#if defined(__EMSCRIPTEN__)
    /* the AudioContext says, whether a gesture or an autoplay policy let it run */
    ma_device* output = ma_engine_get_device(&device->engine);
    int running = EM_ASM_INT({
        var device = window.miniaudio && miniaudio.get_device_by_index($0);
        return device && device.webaudio && device.webaudio.state === 'running' ? 1 : 0;
    }, output->webaudio.deviceIndex);
    return !running;
#else
    return (int)ma_atomic_load_32(&device->suspended);
#endif
}

SUNDEX_API void sundex_device_set_volume(sundex_device* device, float volume)
{
    ma_engine_set_volume(&device->engine, volume);
}

static ma_uint32 sundex_bytes_per_frame(ma_format format, ma_uint32 channels)
{
    return ma_get_bytes_per_sample(format) * channels;
}

/* format: 1 u8, 2 s16, 5 f32 - miniaudio's own ma_format values. */
SUNDEX_API int sundex_buffer_create(sundex_device* device, const void* samples, int format, int channels,
                                    int sample_rate, long long frames, sundex_buffer** out)
{
    sundex_buffer* buffer;
    size_t size = (size_t)frames * sundex_bytes_per_frame((ma_format)format, (ma_uint32)channels);

    *out = NULL;
    buffer = (sundex_buffer*)calloc(1, sizeof(sundex_buffer));
    if (buffer == NULL) return MA_OUT_OF_MEMORY;

    buffer->data = malloc(size > 0 ? size : 1);
    if (buffer->data == NULL)
    {
        free(buffer);
        return MA_OUT_OF_MEMORY;
    }

    memcpy(buffer->data, samples, size);
    buffer->device = device;
    buffer->format = (ma_format)format;
    buffer->channels = (ma_uint32)channels;
    buffer->sample_rate = (ma_uint32)sample_rate;
    buffer->frames = (ma_uint64)frames;
    *out = buffer;
    return MA_SUCCESS;
}

static void sundex_voice_unlink_free(sundex_voice* voice)
{
    sundex_buffer* buffer = voice->owner;
    ma_sound_uninit(&voice->sound);
    ma_audio_buffer_ref_uninit(&voice->ref);
    if (voice->prev != NULL) voice->prev->next = voice->next;
    else buffer->voices = voice->next;
    if (voice->next != NULL) voice->next->prev = voice->prev;
    free(voice);
}

static int sundex_voice_new(sundex_buffer* buffer, int once, sundex_voice** out)
{
    sundex_voice* voice;
    ma_result result;

    *out = NULL;
    voice = (sundex_voice*)calloc(1, sizeof(sundex_voice));
    if (voice == NULL) return MA_OUT_OF_MEMORY;

    result = ma_audio_buffer_ref_init(buffer->format, buffer->channels, buffer->data, buffer->frames, &voice->ref);
    if (result != MA_SUCCESS)
    {
        free(voice);
        return result;
    }

    /* read by ma_sound_init_from_data_source, which resamples from it to the engine's rate */
    voice->ref.sampleRate = buffer->sample_rate;
    result = ma_sound_init_from_data_source(&buffer->device->engine, &voice->ref, MA_SOUND_FLAG_NO_SPATIALIZATION,
                                            NULL, &voice->sound);
    if (result != MA_SUCCESS)
    {
        ma_audio_buffer_ref_uninit(&voice->ref);
        free(voice);
        return result;
    }

    voice->owner = buffer;
    voice->once = once;
    voice->next = buffer->voices;
    if (buffer->voices != NULL) buffer->voices->prev = voice;
    buffer->voices = voice;
    *out = voice;
    return MA_SUCCESS;
}

/* Frees every voice on the buffer, then the buffer. */
SUNDEX_API void sundex_buffer_free(sundex_buffer* buffer)
{
    while (buffer->voices != NULL) sundex_voice_unlink_free(buffer->voices);
    free(buffer->data);
    free(buffer);
}

/* Fire and forget. Finished one-shots are freed here, on the next call, never on the audio thread. */
SUNDEX_API int sundex_buffer_play_once(sundex_buffer* buffer, float volume, float pan)
{
    sundex_voice* voice = buffer->voices;
    ma_result result;

    while (voice != NULL)
    {
        sundex_voice* next = voice->next;
        if (voice->once && ma_sound_at_end(&voice->sound)) sundex_voice_unlink_free(voice);
        voice = next;
    }

    result = sundex_voice_new(buffer, 1, &voice);
    if (result != MA_SUCCESS) return result;

    ma_sound_set_volume(&voice->sound, volume);
    ma_sound_set_pan(&voice->sound, pan);
    return ma_sound_start(&voice->sound);
}

/* Starts stopped at frame 0. */
SUNDEX_API int sundex_voice_create(sundex_buffer* buffer, sundex_voice** out)
{
    return sundex_voice_new(buffer, 0, out);
}

SUNDEX_API void sundex_voice_free(sundex_voice* voice)
{
    sundex_voice_unlink_free(voice);
}

SUNDEX_API long long sundex_voice_position(sundex_voice* voice)
{
    ma_uint64 cursor = 0;
    ma_sound_get_cursor_in_pcm_frames(&voice->sound, &cursor);
    return (long long)cursor;
}

SUNDEX_API void sundex_voice_seek(sundex_voice* voice, long long frame)
{
    ma_sound_seek_to_pcm_frame(&voice->sound, (ma_uint64)frame);
}

SUNDEX_API void sundex_voice_set_paused(sundex_voice* voice, int paused)
{
    if (paused) ma_sound_stop(&voice->sound);
    else ma_sound_start(&voice->sound);
}

SUNDEX_API void sundex_voice_set_volume(sundex_voice* voice, float volume)
{
    ma_sound_set_volume(&voice->sound, volume);
}

SUNDEX_API void sundex_voice_set_pan(sundex_voice* voice, float pan)
{
    ma_sound_set_pan(&voice->sound, pan);
}

/*
    The push stream's data source. It never reports an end: when the queue runs dry it hands the
    mixer silence without moving the read position, so the playhead stalls on the last frame put
    and plays on from there with the next put, as BASS's push streams did.
*/
static ma_result sundex_stream_on_read(ma_data_source* source, void* out, ma_uint64 frame_count, ma_uint64* frames_read)
{
    sundex_stream* stream = (sundex_stream*)source;
    float* frames = (float*)out;
    ma_uint64 read, available, count, at, first;

    ma_spinlock_lock(&stream->lock);
    read = stream->read;
    available = ma_atomic_load_64(&stream->written) - read;
    count = available < frame_count ? available : frame_count;
    at = read % stream->capacity;
    first = stream->capacity - at < count ? stream->capacity - at : count;

    memcpy(frames, stream->ring + at * stream->channels, (size_t)(first * stream->channels * sizeof(float)));
    memcpy(frames + first * stream->channels, stream->ring,
           (size_t)((count - first) * stream->channels * sizeof(float)));
    ma_atomic_store_64(&stream->read, read + count);
    ma_spinlock_unlock(&stream->lock);

    memset(frames + count * stream->channels, 0, (size_t)((frame_count - count) * stream->channels * sizeof(float)));
    *frames_read = frame_count;
    return MA_SUCCESS;
}

static ma_result sundex_stream_on_seek(ma_data_source* source, ma_uint64 frame)
{
    (void)source;
    (void)frame;
    return MA_NOT_IMPLEMENTED;
}

static ma_result sundex_stream_on_format(ma_data_source* source, ma_format* format, ma_uint32* channels,
                                         ma_uint32* sample_rate, ma_channel* channel_map, size_t channel_map_cap)
{
    sundex_stream* stream = (sundex_stream*)source;
    *format = ma_format_f32;
    *channels = stream->channels;
    *sample_rate = stream->sample_rate;
    if (channel_map != NULL)
        ma_channel_map_init_standard(ma_standard_channel_map_default, channel_map, channel_map_cap, stream->channels);
    return MA_SUCCESS;
}

static ma_data_source_vtable sundex_stream_vtable =
{
    sundex_stream_on_read,
    sundex_stream_on_seek,
    sundex_stream_on_format,
    NULL,
    NULL,
    NULL,
    0
};

/* Starts stopped and empty. */
SUNDEX_API int sundex_stream_create(sundex_device* device, int sample_rate, int channels, long long capacity,
                                    sundex_stream** out)
{
    sundex_stream* stream;
    ma_data_source_config config;
    ma_result result;

    *out = NULL;
    if (capacity <= 0 || channels <= 0) return MA_INVALID_ARGS;
    stream = (sundex_stream*)calloc(1, sizeof(sundex_stream));
    if (stream == NULL) return MA_OUT_OF_MEMORY;

    stream->ring = (float*)malloc((size_t)capacity * (size_t)channels * sizeof(float));
    if (stream->ring == NULL)
    {
        free(stream);
        return MA_OUT_OF_MEMORY;
    }

    stream->capacity = (ma_uint64)capacity;
    stream->channels = (ma_uint32)channels;
    stream->sample_rate = (ma_uint32)sample_rate;

    config = ma_data_source_config_init();
    config.vtable = &sundex_stream_vtable;
    result = ma_data_source_init(&config, &stream->ds);
    if (result == MA_SUCCESS)
        result = ma_sound_init_from_data_source(&device->engine, &stream->ds, MA_SOUND_FLAG_NO_SPATIALIZATION, NULL,
                                                &stream->sound);
    if (result != MA_SUCCESS)
    {
        ma_data_source_uninit(&stream->ds);
        free(stream->ring);
        free(stream);
        return result;
    }

    *out = stream;
    return MA_SUCCESS;
}

SUNDEX_API void sundex_stream_free(sundex_stream* stream)
{
    ma_sound_uninit(&stream->sound);
    ma_data_source_uninit(&stream->ds);
    free(stream->ring);
    free(stream);
}

/* Queues whole frames after everything put so far. Returns the frames that fitted. */
SUNDEX_API long long sundex_stream_put(sundex_stream* stream, const float* interleaved, long long frames)
{
    ma_uint64 written = ma_atomic_load_64(&stream->written);
    ma_uint64 space = stream->capacity - (written - ma_atomic_load_64(&stream->read));
    ma_uint64 count = (ma_uint64)frames < space ? (ma_uint64)frames : space;
    ma_uint64 at = written % stream->capacity;
    ma_uint64 first = stream->capacity - at < count ? stream->capacity - at : count;

    memcpy(stream->ring + at * stream->channels, interleaved, (size_t)(first * stream->channels * sizeof(float)));
    memcpy(stream->ring, interleaved + first * stream->channels,
           (size_t)((count - first) * stream->channels * sizeof(float)));
    /* publish the frames only once they are in the ring */
    ma_atomic_store_64(&stream->written, written + count);
    return (long long)count;
}

SUNDEX_API long long sundex_stream_queued(sundex_stream* stream)
{
    return (long long)(ma_atomic_load_64(&stream->written) - ma_atomic_load_64(&stream->read));
}

SUNDEX_API long long sundex_stream_played(sundex_stream* stream)
{
    long long played;
    ma_spinlock_lock(&stream->lock);
    played = stream->base + (long long)(stream->read - stream->start);
    ma_spinlock_unlock(&stream->lock);
    return played;
}

/* Drops everything not played yet and restarts the playhead at base. Must not overlap a put. */
SUNDEX_API void sundex_stream_flush(sundex_stream* stream, long long base)
{
    ma_spinlock_lock(&stream->lock);
    stream->start = ma_atomic_load_64(&stream->written);
    ma_atomic_store_64(&stream->read, stream->start);
    stream->base = base;
    ma_spinlock_unlock(&stream->lock);
}

SUNDEX_API void sundex_stream_set_paused(sundex_stream* stream, int paused)
{
    if (paused) ma_sound_stop(&stream->sound);
    else ma_sound_start(&stream->sound);
}

SUNDEX_API void sundex_stream_set_volume(sundex_stream* stream, float volume)
{
    ma_sound_set_volume(&stream->sound, volume);
}

/* Resamples: 2 plays twice as fast and an octave up. */
SUNDEX_API void sundex_stream_set_rate(sundex_stream* stream, float rate)
{
    ma_sound_set_pitch(&stream->sound, rate);
}
