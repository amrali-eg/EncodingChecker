namespace EncodingChecker.Tests;

/// <summary>
/// Runs a test body on the single-threaded apartment required by Windows Forms.
/// Keep every UI test on this helper: production callbacks may be concurrent, but
/// creating and inspecting controls must happen on one STA thread.
/// </summary>
internal static class UiTest
{
    internal static void OnStaThread(Action body)
    {
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                body();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the UI test did not finish");

        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"the UI test threw: {failure}");
    }
}
