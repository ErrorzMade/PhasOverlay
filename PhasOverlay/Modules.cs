namespace PhasOverlay
{
    /// <summary>Auto shows a module only while the thing it reports is happening.</summary>
    public enum ModuleMode
    {
        Off = 0,
        Auto = 1,
        Always = 2
    }

    /// <summary>
    /// Index into <see cref="MainWindow.ModuleModes"/>. The numbers are the order the modes are
    /// written to settings.txt, so they are part of the file format and must not be reordered.
    /// </summary>
    public enum ModuleId
    {
        Smudge = 0,
        Cooldown = 1,
        Hunt = 2,
        Obambo = 3,
        SpeedTap = 4,
        BloodMoon = 5,
        Cursed = 6,
        Evidence = 7,
        Ghosts = 8
    }
}
