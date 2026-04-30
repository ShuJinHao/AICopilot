namespace AICopilot.SharedKernel.Ai;

public enum ChatExposureMode
{
    Disabled = 0,
    ObserveOnly = 1,
    Advisory = 2,
    Control = 3
}

public static class ChatExposureModeExtensions
{
    public static bool CanExposeInChat(this ChatExposureMode mode)
    {
        return mode is ChatExposureMode.ObserveOnly or ChatExposureMode.Advisory;
    }
}
