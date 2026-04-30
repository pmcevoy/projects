namespace ProbHammer.Core.Simulation;

// Tracks remaining wounds per model. Excess damage on the current model is lost (no spillover).
public struct WoundPool
{
    private readonly int[] _modelWounds;
    private int _currentModel;

    public int Kills { get; private set; }

    public WoundPool(int modelCount, int woundsPerModel)
    {
        _modelWounds = new int[Math.Max(0, modelCount)];
        Array.Fill(_modelWounds, woundsPerModel);
        _currentModel = 0;
        Kills = 0;
    }

    // Apply damage from one failed save to the current model. Excess beyond remaining wounds is lost.
    public void Apply(int damage)
    {
        if (damage <= 0 || _currentModel >= _modelWounds.Length) return;
        int capped = Math.Min(damage, _modelWounds[_currentModel]);
        _modelWounds[_currentModel] -= capped;
        if (_modelWounds[_currentModel] == 0)
        {
            Kills++;
            _currentModel++;
        }
    }
}
