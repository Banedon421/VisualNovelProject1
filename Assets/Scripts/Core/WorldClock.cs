using System;

// Two clocks, deliberately: the narrative clock (which scene/chapter you're
// in) lives entirely in Ink's own story position and isn't duplicated here.
// This class only tracks the coarse "world calendar" -- the unit you need
// for aging, death by old age, and generational time-skips. It advances
// only when the narrative explicitly says so (e.g. an ink line calling
// advance_years), never automatically on its own -- see the two-clock
// discussion in README_SETUP.md.
public class WorldClock
{
    public int CurrentYear { get; private set; }

    public event Action<int> OnYearAdvanced;

    public WorldClock(int startYear)
    {
        CurrentYear = startYear;
    }

    public void AdvanceYears(int count)
    {
        if (count <= 0) return;
        CurrentYear += count;
        OnYearAdvanced?.Invoke(CurrentYear);
    }

    // For save/load.
    public void Restore(int year) => CurrentYear = year;
}
