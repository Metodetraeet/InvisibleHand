using System;
using System.Collections.Generic;

namespace InvisibleHand;

public sealed class BootstrapPlan
{
    public bool InstallCriticals;
    public List<string> Failures;
}

public static class CompatibilityBootstrapCore
{
    public static BootstrapPlan Validate(
        IEnumerable<string> targetNames,
        Func<string, string> resolve)
    {
        var failures = new List<string>();
        foreach (string name in targetNames)
        {
            string reason;
            try
            {
                reason = resolve(name);
            }
            catch (Exception e)
            {
                reason = "resolver threw: " + e.Message;
            }
            if (reason != null)
            {
                failures.Add(name + ": " + reason);
            }
        }
        return new BootstrapPlan
        {
            InstallCriticals = failures.Count == 0,
            Failures = failures
        };
    }
}
