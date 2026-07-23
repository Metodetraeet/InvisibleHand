namespace InvisibleHand;

public static class CompatibilityStatus
{
    //true only after every critical patch installed and verified
    public static bool EngineEnabled;

    //set on critical failure, shown once in the in-game dialog
    public static string FailureSummary;
    public static bool FailureDialogShown;
}
