// small animation that plays when the card appears
var shown = State(0.0);
Ease.To(320, progress => shown.Value = progress);
