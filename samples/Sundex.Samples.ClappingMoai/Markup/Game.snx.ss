// The overlay over the island: frosted cards that blur the dusk behind them, and the score.
// Edit this while the game runs in Debug and the overlay restyles itself.

var ink = #fff6e8
var mist = #e9d6ecb8
var glass = #2a1b3d80
var rim = #ffffff33
var key_fill = #ffffff14
var gold = #ffcf86
var dusk = #2b1b4a
var coral = #ff7a59
var coral_lit = #ff9470

animation bob {
    timing-function = "ease-in-out";
    duration = 1400ms;
    keyframes = !keyframes [
        0% = { transform = vec2(0, 0) },
        100% = { transform = vec2(0, -8px); loop = "invert" }
    ];
}

animation pulse {
    timing-function = "ease-in-out";
    duration = 900ms;
    keyframes = !keyframes [
        0% = { opacity = 1 },
        100% = { opacity = 0.7; loop = "invert" }
    ];
}

component label {
    font = "Lato Bold";
    font-color = $ink;
}

class screen {
    width = 100%;
    height = 100%;
    direction = "vertical";
    align-items = "center";
    justify = "center";
}

class score {
    position = "absolute";
    top = 36px;
    left = 0px;
    right = 0px;
    text-align = "center";
    font-size = 64px;
}

class card {
    width = 340px;
    direction = "vertical";
    align-items = "center";
    gap = 10px;
    padding = 28px;
    border-radius = 22px;
    border-width = 1px;
    border-color = $rim;
    fill = $glass;
    backdrop-blur = 4;
    glow-color = #ffb36b26;
}

class emblem {
    width = 96px;
    height = 96px;
    animations = ["bob"];
}

class title {
    font-size = 38px;
    letter-spacing = -0.02;
}

class tagline {
    font = "Lato Regular";
    font-size = 16px;
    font-color = $mist;
}

class keys {
    margin-top = 8px;
    direction = "horizontal";
    align-items = "center";
    gap = 8px;
}

class key {
    padding-left = 10px;
    padding-right = 10px;
    padding-top = 4px;
    padding-bottom = 4px;
    border-radius = 8px;
    border-width = 1px;
    border-color = $rim;
    fill = $key_fill;
}

class key-text { font-size = 14px; }
class or { font = "Lato Regular"; font-size = 14px; font-color = $mist; }
class best { margin-top = 4px; font-size = 15px; font-color = $gold; }

class heading { font-size = 34px; }

class stats {
    margin-top = 4px;
    direction = "horizontal";
    gap = 36px;
}

class stat {
    direction = "vertical";
    align-items = "center";
    gap = 2px;
}

class stat-label { font-size = 12px; letter-spacing = 0.12; font-color = $mist; }
class stat-value { font-size = 40px; }

class badge {
    padding-left = 12px;
    padding-right = 12px;
    padding-top = 4px;
    padding-bottom = 4px;
    border-radius = 14px;
    fill = $gold;
    animations = ["pulse"];
}

class badge-text { font-size = 13px; font-color = $dusk; }

class again {
    margin-top = 10px;
    min-height = 46px;
    padding-left = 26px;
    padding-right = 26px;
    direction = "horizontal";
    align-items = "center";
    justify = "center";
    border-radius = 12px;
    fill = $coral;
    cursor = "pointer";
    state[hovered] = { fill = $coral_lit; };
}

class again-text { font-size = 17px; }
class hint { font = "Lato Regular"; font-size = 13px; font-color = $mist; }
