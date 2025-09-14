namespace Content.IntegrationTests;

[SetUpFixture]
public sealed class PoolManagerTestEventHandler
{
    // Default total test time before gracefully shutting down the pool. Can be overridden via env var SS14_TEST_MAX_MINUTES.
    private static TimeSpan MaximumTotalTestingTimeLimit
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("SS14_TEST_MAX_MINUTES");
            if (!string.IsNullOrWhiteSpace(env) && double.TryParse(env, out var minutes) && minutes > 0 && minutes < 300)
                return TimeSpan.FromMinutes(minutes);

            // Default increased to reduce premature pool shutdowns on slower/dev machines.
            return TimeSpan.FromMinutes(60);
        }
    }
    private static TimeSpan HardStopTimeLimit => MaximumTotalTestingTimeLimit.Add(TimeSpan.FromMinutes(1));

    [OneTimeSetUp]
    public void Setup()
    {
        PoolManager.Startup();
        // If the tests seem to be stuck, we try to end it semi-nicely
        _ = Task.Delay(MaximumTotalTestingTimeLimit).ContinueWith(_ =>
        {
            // This can and probably will cause server/client pairs to shut down MID test, and will lead to really confusing test failures.
            TestContext.Error.WriteLine($"\n\n{nameof(PoolManagerTestEventHandler)}: ERROR: Tests exceeded time limit ({MaximumTotalTestingTimeLimit}). Shutting down all tests. This may lead to weird failures/exceptions.\n\n");
            PoolManager.Shutdown();
        });

        // If ending it nicely doesn't work within a minute, we do something a bit meaner.
        _ = Task.Delay(HardStopTimeLimit).ContinueWith(_ =>
        {
            var deathReport = PoolManager.DeathReport();
            Environment.FailFast($"Tests exceeded hard-stop time limit ({HardStopTimeLimit}).\nDeath Report:\n{deathReport}");
        });
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        PoolManager.Shutdown();
    }
}
