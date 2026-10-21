using System.Globalization;
using System.Text;

namespace YahtzeeSim.Analysis.Data;

/// <summary>One row of exports/actions.csv (see migrations/0002_game_actions.sql).</summary>
public sealed record ActionRow(
    long Id,
    string Nickname,
    string UserId,
    string GameId,
    int Seq,
    string Mode,
    string Action,
    long OccurredAt,
    int Turn,
    int RollNumber,
    string Dice,
    int? DieId,
    int? GameIndex,
    string? Category,
    int? Points,
    bool YahtzeeBonus);

/// <summary>One row of exports/scores.csv: a finished game submitted to the leaderboard.</summary>
public sealed record ScoreRow(long Id, string Nickname, string UserId, string GameId, string Mode, int Total, long PlayedAt);

/// <summary>One row of exports/users.csv.</summary>
public sealed record UserRow(string Id, string Nickname, long CreatedAt);

/// <summary>The three tables written by scripts/export-game-data.ps1.</summary>
public sealed record ExportData(IReadOnlyList<UserRow> Users, IReadOnlyList<ScoreRow> Scores, IReadOnlyList<ActionRow> Actions)
{
    public static ExportData Load(string directory)
    {
        var users = Read(Path.Combine(directory, "users.csv"), r => new UserRow(r["id"], r["nickname"], Long(r["created_at"])), optional: true);
        var scores = Read(Path.Combine(directory, "scores.csv"), r => new ScoreRow(
            Long(r["id"]), r["nickname"], r["user_id"], r["client_game_id"], r["mode"], Int(r["total"]), Long(r["played_at"])));
        var actions = Read(Path.Combine(directory, "actions.csv"), r => new ActionRow(
            Long(r["id"]),
            r["nickname"],
            r["user_id"],
            r["client_game_id"],
            Int(r["seq"]),
            r["mode"],
            r["action"],
            Long(r["occurred_at"]),
            Int(r["turn"]),
            Int(r["roll_number"]),
            r["dice"],
            NullableInt(r["die_id"]),
            NullableInt(r["game_index"]),
            string.IsNullOrEmpty(r["category"]) ? null : r["category"],
            NullableInt(r["points"]),
            r["yahtzee_bonus"] is "1" or "true" or "True"));
        return new ExportData(users, scores, actions);
    }

    private static List<T> Read<T>(string path, Func<IReadOnlyDictionary<string, string>, T> map, bool optional = false)
    {
        if (!File.Exists(path))
        {
            if (optional)
            {
                return [];
            }

            throw new FileNotFoundException($"Missing export file {path}. Run scripts/export-game-data.ps1 first.", path);
        }

        var rows = Csv.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (rows.Count == 0)
        {
            return [];
        }

        var header = rows[0];
        var result = new List<T>(rows.Count - 1);
        for (int i = 1; i < rows.Count; i++)
        {
            if (rows[i].Count == 1 && rows[i][0].Length == 0)
            {
                continue;
            }

            var record = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int c = 0; c < header.Count; c++)
            {
                record[header[c]] = c < rows[i].Count ? rows[i][c] : string.Empty;
            }

            try
            {
                result.Add(map(record));
            }
            catch (Exception ex) when (ex is FormatException or KeyNotFoundException or OverflowException)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} line {i + 1}: {ex.Message}", ex);
            }
        }

        return result;
    }

    private static int Int(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static long Long(string s) => long.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static int? NullableInt(string s) => string.IsNullOrEmpty(s) ? null : Int(s);
}

/// <summary>Minimal RFC 4180 CSV parser (quoted fields, doubled quotes, CRLF or LF).</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        int i = 0;
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            i = 1;
        }

        for (; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (ch == '\n' || ch == '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(ch);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    public static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
}
