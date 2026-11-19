using UnityEngine;

// How each weapon is handled by the Tactical arms on the Basic FPS clips (ElderboomArmsDriver):
//  - fire: the family's Fire clip on every shot (semi-automatic handguns, whose clip carries the slide and the
//    break), or only the additive recoil below (automatic weapons, semi-automatic shotgun), optionally followed by a
//    procedural pump stroke (pump shotgun);
//  - reload: the family's reload clips (magazine), the tube sequence (start, one loop per shell, end), or a procedural
//    reload on top of the idle where no clip fits the weapon (revolver, heavy weapons);
//  - recoil: an additive spring on the arms and the weapon, per weapon;
//  - aim: eye relief behind the sight line.
public static class ElderboomWeaponHandling
{
    public enum Fire { Clip, Additive }
    public enum Reload { Clip, Tube, Revolver, Heavy }

    public sealed class Profile
    {
        public Fire fire = Fire.Additive;
        public bool pump;                    // procedural pump stroke after each shot
        public Reload reload = Reload.Clip;
        public float reloadSeconds = 2.8f;   // procedural reloads
        // additive recoil: metres back, degrees of muzzle climb / random yaw / random roll, spring frequency and damping
        public float back = .015f, pitch = 1.4f, yaw = .5f, roll = 1f, frequency = 20f, damping = .6f;
        public float relief = .11f;          // iron sights, metres behind the rear sight
        public bool lockSights = true;       // false for heavy weapons: braced FOV zoom without locking sights onto the eye
        public float idleSpeed = 1f;         // heavy weapons breathe slower
    }

    public static Profile For(ElderboomWeaponDefinition d)
    {
        string id = d != null ? d.id : "";
        string cat = d != null ? d.category : ElderboomIds.Category.AssaultRifle;
        switch (id)
        {
            case "m4": return new Profile { fire = Fire.Additive, back = .016f, pitch = 1.5f, yaw = .55f, roll = 1.1f, frequency = 22f, damping = .58f, relief = .14f };
            case "mp7": return new Profile { fire = Fire.Additive, back = .011f, pitch = 1.05f, yaw = .5f, roll = .8f, frequency = 24f, damping = .6f, relief = .20f };
            case "minigun": return new Profile { fire = Fire.Additive, back = .005f, pitch = .3f, yaw = .35f, roll = .25f, frequency = 30f, damping = .7f, reload = Reload.Heavy, reloadSeconds = 3.4f, lockSights = false, idleSpeed = .8f };
            case "flame": return new Profile { fire = Fire.Additive, back = .0015f, pitch = .08f, yaw = .12f, roll = .05f, frequency = 26f, damping = .8f, reload = Reload.Heavy, reloadSeconds = 3.0f, lockSights = false, idleSpeed = .85f };
            case "grenade": return new Profile { fire = Fire.Additive, back = .05f, pitch = 5.5f, yaw = 1.2f, roll = 2.5f, frequency = 14f, damping = .55f, reload = Reload.Heavy, reloadSeconds = 3.1f, lockSights = false, idleSpeed = .9f };
            case "shotgun": return new Profile { fire = Fire.Additive, reload = Reload.Tube, back = .05f, pitch = 6f, yaw = 1.4f, roll = 3f, frequency = 15f, damping = .55f, relief = .13f };
            case "mp133": return new Profile { fire = Fire.Additive, pump = true, reload = Reload.Tube, back = .055f, pitch = 6.5f, yaw = 1.5f, roll = 3f, frequency = 15f, damping = .55f, relief = .34f };
            case "glock19": return new Profile { fire = Fire.Clip, back = .008f, pitch = 1.2f, yaw = .4f, roll = .6f, frequency = 26f, damping = .6f, relief = .26f };
            case "rhino": return new Profile { fire = Fire.Clip, reload = Reload.Revolver, reloadSeconds = 2.9f, back = .018f, pitch = 3f, yaw = .6f, roll = 1.2f, frequency = 18f, damping = .55f, relief = .26f };
        }
        if (cat == ElderboomIds.Category.Handgun) return new Profile { fire = Fire.Clip, back = .01f, pitch = 1.5f, relief = .26f };
        if (cat == ElderboomIds.Category.Shotgun) return new Profile { reload = Reload.Tube, back = .05f, pitch = 6f, yaw = 1.4f, roll = 3f, frequency = 15f, damping = .55f, relief = .13f };
        return new Profile();
    }

    // Eye relief for the sight in use: red dots / holographics sit a little further, magnified scopes closer.
    public static float Relief(Profile p, Transform weapon)
    {
        var optic = weapon != null ? weapon.GetComponentInChildren<ElderboomOpticSight>(true) : null;
        if (optic == null) return p.relief;
        return optic.magnification >= 3f ? .085f : Mathf.Max(p.relief, .14f);
    }
}
