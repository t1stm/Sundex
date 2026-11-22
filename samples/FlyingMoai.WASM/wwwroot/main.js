const canvas = document.getElementById('sundex');
const status = document.getElementById('status');
const bestKey = 'FlyingMoai.best';
const options = new URLSearchParams(location.search);
const verify = options.has('verify');
const benchmark = options.has('benchmark');
let stopped = false;

function fail(message) {
    stopped = true;
    status.hidden = false;
    status.textContent = message;
    status.dataset.result = 'fail';
}

function canvasSize() {
    const width = Math.max(1, canvas.clientWidth);
    const height = Math.max(1, canvas.clientHeight);
    const physicalWidth = Math.max(1, Math.round(width * devicePixelRatio));
    const physicalHeight = Math.max(1, Math.round(height * devicePixelRatio));
    if (canvas.width !== physicalWidth) canvas.width = physicalWidth;
    if (canvas.height !== physicalHeight) canvas.height = physicalHeight;
    return [width, height, physicalWidth, physicalHeight];
}

function readBest() {
    try {
        const best = Number(localStorage.getItem(bestKey));
        return Number.isInteger(best) && best >= 0 && best <= 2147483647 ? best : 0;
    } catch {
        return 0;
    }
}

function saveBest(best) {
    try {
        localStorage.setItem(bestKey, String(best));
    } catch {
        // The best score stays available for this session when storage is blocked.
    }
}

async function start() {
    const { dotnet } = await import('./_framework/dotnet.js');
    const runtime = await dotnet.create();
    runtime.setModuleImports('moai', { saveBest });
    await runtime.runMain(runtime.getConfig().mainAssemblyName, []);
    const { FlyingMoai: { WASM: { Program: game } } } = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    let size = canvasSize();
    game.Start(...size, readBest());

    let pointer = null;
    function point(event, down, pressed, released) {
        const bounds = canvas.getBoundingClientRect();
        game.Pointer(event.clientX - bounds.left, event.clientY - bounds.top, down, pressed, released);
    }
    function resetPointer() {
        if (stopped) return;
        const held = pointer !== null;
        pointer = null;
        game.Pointer(-1, -1, false, false, held);
    }
    canvas.addEventListener('pointerdown', event => {
        if (stopped || pointer !== null || event.button !== 0) return;
        canvas.focus({ preventScroll: true });
        pointer = event.pointerId;
        canvas.setPointerCapture(pointer);
        point(event, true, true, false);
    });
    canvas.addEventListener('pointermove', event => {
        if (!stopped && (pointer === null || event.pointerId === pointer)) point(event, pointer !== null, false, false);
    });
    canvas.addEventListener('pointerup', event => {
        if (stopped || event.pointerId !== pointer) return;
        pointer = null;
        point(event, false, false, true);
    });
    canvas.addEventListener('pointerleave', () => { if (pointer === null) resetPointer(); });
    canvas.addEventListener('lostpointercapture', event => { if (event.pointerId === pointer) resetPointer(); });
    canvas.addEventListener('pointercancel', event => { if (event.pointerId === pointer) resetPointer(); });
    window.addEventListener('blur', resetPointer);
    window.addEventListener('keydown', event => {
        if (stopped || event.repeat || event.ctrlKey || event.altKey || event.metaKey) return;
        if (['Space', 'ArrowUp', 'KeyW', 'Enter'].includes(event.code)) {
            event.preventDefault();
            game.Clap();
        }
    });

    let lastFrame;
    let frameCount = 0;
    function animate(now) {
        if (stopped) return;
        try {
            const next = canvasSize();
            if (next.some((value, index) => value !== size[index])) {
                size = next;
                game.Resize(...size);
            }
            const measure = benchmark && frameCount >= 30 && frameCount < 150;
            const beforeDraw = measure ? performance.now() : 0;
            const hand = game.Draw(lastFrame === undefined ? 0 : (now - lastFrame) / 1000, verify);
            if (measure) performance.measure('flying-moai-frame', { start: beforeDraw, end: performance.now() });
            const cursor = hand ? 'pointer' : 'default';
            if (canvas.style.cursor !== cursor) canvas.style.cursor = cursor;
            if (verify) canvas.dataset.screen = game.Screen();
            if (lastFrame === undefined) {
                performance.mark('flying-moai-first-frame');
                status.hidden = true;
                status.dataset.result = 'ready';
            }
            frameCount++;
            lastFrame = now;
            requestAnimationFrame(animate);
        } catch (error) {
            fail(error.message ?? error);
            console.error(error);
        }
    }

    canvas.addEventListener('webglcontextlost', () => fail('Graphics context lost. Reload to play again.'));
    requestAnimationFrame(animate);
}

start().catch(error => {
    fail(error.message ?? error);
    console.error(error);
});
