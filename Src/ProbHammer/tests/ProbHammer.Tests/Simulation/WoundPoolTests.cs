using FluentAssertions;
using ProbHammer.Core.Simulation;

namespace ProbHammer.Tests.Simulation;

public class WoundPoolTests
{
    [Fact]
    public void Apply_ExactWounds_KillsModel()
    {
        var pool = new WoundPool(1, 2);
        pool.Apply(2);
        pool.Kills.Should().Be(1);
    }

    [Fact]
    public void Apply_ExcessDamage_IsLost_NotCarriedOver()
    {
        // 3-damage weapon vs 1-wound model: kills the model, excess 2 is lost
        var pool = new WoundPool(2, 1);
        pool.Apply(3);
        pool.Kills.Should().Be(1); // one model killed
        pool.Apply(1);             // second apply goes to second model
        pool.Kills.Should().Be(2);
    }

    [Fact]
    public void Apply_DamageCapAtCurrentModel_ExcessLost()
    {
        // Model has 2 wounds; 3-damage weapon → caps at 2, kills model, 1 excess lost
        var pool = new WoundPool(2, 2);
        pool.Apply(3);
        pool.Kills.Should().Be(1);
        // Second model still has full 2 wounds
        pool.Apply(1); // partial damage
        pool.Kills.Should().Be(1); // not dead yet
        pool.Apply(1); // finishes model
        pool.Kills.Should().Be(2);
    }

    [Fact]
    public void Apply_AfterAllModelsDead_DoesNothing()
    {
        var pool = new WoundPool(1, 1);
        pool.Apply(1);
        pool.Kills.Should().Be(1);
        pool.Apply(5); // pool exhausted — should be a no-op
        pool.Kills.Should().Be(1);
    }

    [Fact]
    public void Apply_ZeroDamage_NoEffect()
    {
        var pool = new WoundPool(1, 3);
        pool.Apply(0);
        pool.Kills.Should().Be(0);
    }

    [Fact]
    public void MultipleModels_IndependentTracking()
    {
        var pool = new WoundPool(3, 2);
        pool.Apply(2); // kills model 1
        pool.Apply(1); // partial on model 2
        pool.Apply(1); // kills model 2
        pool.Apply(2); // kills model 3
        pool.Kills.Should().Be(3);
    }
}
