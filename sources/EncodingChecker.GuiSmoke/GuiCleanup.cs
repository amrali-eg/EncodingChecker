namespace EncodingChecker.GuiSmoke;

/// <summary>Cleanup must report its own errors without replacing the phase's error.</summary>
internal static class GuiCleanup
{
    internal static void Run(Action close, Action release, Action<Exception> report)
    {
        try
        {
            close();
        }
        catch (Exception ex)
        {
            report(ex);
        }
        finally
        {
            try
            {
                release();
            }
            catch (Exception ex)
            {
                report(ex);
            }
        }
    }
}
