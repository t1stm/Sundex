# Sundex.Core

Planes and animations on top of [Sundex.Engine](../Sundex.Engine/README.md). Reference it for a game with planes, text and a scene loop.

- `Renderable` is the base of everything drawn.
- `Renderer/Planes/` holds colored, gradient, textured and box planes. `BoxPlane` adds borders, glow and a blurred backdrop.
- `Renderer/Planes/Blur/` holds picom's dual Kawase blur.
- `Animations/` holds keyframed animations and their easing.
- `Assets/Shaders/` holds the planes' shaders.
