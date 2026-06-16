namespace DesktopPet.App.Cloud;

public sealed record VoiceSynthesisRequest(string Text);

public sealed record VoiceSynthesisResult(byte[] AudioBytes, string AudioFormat);
