// Shared identifiers. Operator ids match ElderboomOperatorDefinition.id; weapon
// categories match the "category" field of Resources/Elderboom/Weapons.json;
// station categories match ElderboomStation.category in the scene.
public static class ElderboomIds
{
    public const string Soph = "SOPH";
    public const string Eli = "ELI";
    public const string Tactical = "TACTICAL";

    public static class Category
    {
        public const string Melee = "melee";
        public const string Handgun = "handgun";
        public const string Shotgun = "shotgun";
        public const string AssaultRifle = "assault_rifle";
    }

    public static class Station
    {
        public const string Ammo = "ammo";
        public const string Medical = "medical";
        public const string Slot = "slot";
    }
}
