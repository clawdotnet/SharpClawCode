using Microsoft.Data.Sqlite;

namespace SharpClaw.Code.UnitTests.Support;

internal static class TestDirectoryCleanup
{
    public static void DeleteIfExists(string path, bool clearSqlitePools = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                if (clearSqlitePools)
                {
                    SqliteConnection.ClearAllPools();
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }
}
