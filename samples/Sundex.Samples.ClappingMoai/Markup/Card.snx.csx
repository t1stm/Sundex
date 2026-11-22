// Shared by both cards: each rises into place as it appears.
var shown = State(0.0);
Ease.To(320, progress => shown.Value = progress);
