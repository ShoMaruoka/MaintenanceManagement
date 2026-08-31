using System.Text;
using MaintenanceManagement.Api.Controllers;
using MaintenanceManagement.Api.Models;

namespace MaintenanceManagement.Api.Tests.Controllers;

public class PilotLogFormatterTests
{
    [Fact]
    public void FormatLogEntry_UsesTimestampLevelMessage()
    {
        var entry = new LogEntry
        {
            Timestamp = "12:34:56",
            Level = "INFO",
            Message = "pilot1 適用開始",
        };

        var line = PilotLogFormatter.FormatLogEntry(entry);

        Assert.Equal("12:34:56 [INFO] pilot1 適用開始", line);
    }

    [Fact]
    public void FormatLog_Append_JoinsEntriesWithNewlines()
    {
        var sb = new StringBuilder();
        PilotLogFormatter.Append(sb, new LogEntry { Timestamp = "01:00:00", Level = "STEP", Message = "開始" });
        PilotLogFormatter.Append(sb, new LogEntry { Timestamp = "01:00:01", Level = "ERROR", Message = "失敗" });

        var expected = "01:00:00 [STEP] 開始" + Environment.NewLine
                     + "01:00:01 [ERROR] 失敗" + Environment.NewLine;
        Assert.Equal(expected, sb.ToString());
    }
}
