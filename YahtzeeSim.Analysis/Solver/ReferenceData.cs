using System.Text.Json;
using System.Text.Json.Serialization;

namespace YahtzeeSim.Analysis.Solver;

/// <summary>
/// The simulated reference and greedy games every report is compared with. Built once with a fixed seed and shipped
/// with the app (wwwroot/analysis/reference.json) so the app never has to simulate; the script builds the same data.
/// </summary>
public sealed record ReferenceData(int RulesHash, int Seed, int Simulations, SimulationSummary Reference, SimulationSummary Greedy)
{
    public static ReferenceData Simulate(SheetEvaluator evaluator, int simulations, int seed)
    {
        var simulator = new BotSimulator(evaluator);
        return new ReferenceData(
            CardSolver.RulesHash(evaluator.Tables),
            seed,
            simulations,
            simulator.Run(SheetEvaluator.ReferenceScoring, simulations, seed),
            simulator.Run(PlacementScoring.Greedy, simulations, seed + 1));
    }

    public string ToJson() => JsonSerializer.Serialize(this, AnalysisJsonContext.Default.ReferenceData);

    /// <summary>Reads <see cref="ToJson"/> output. Throws <see cref="InvalidDataException"/> if it was built for other rules.</summary>
    public static ReferenceData FromJson(string json, DiceTables tables)
    {
        ReferenceData? data;
        try
        {
            data = JsonSerializer.Deserialize(json, AnalysisJsonContext.Default.ReferenceData);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The reference data is damaged.", ex);
        }

        if (data == null || data.RulesHash != CardSolver.RulesHash(tables))
        {
            throw new InvalidDataException("The reference data was built for other rules.");
        }

        return data;
    }
}

[JsonSerializable(typeof(ReferenceData))]
internal sealed partial class AnalysisJsonContext : JsonSerializerContext;
