using UnityEngine;

[CreateAssetMenu(menuName = "Elderboom/Abilities/Unavailable Placeholder", fileName = "ElderboomUnavailableAbility")]
public sealed class ElderboomUnavailableAbility : ElderboomAbilityDefinition
{
    public string message = "THIS ABILITY HAS NO AUTHORED ACTION YET";

    public override bool Activate(ElderboomGame game, HDRPSlicePlayer source, ElderboomOperatorDefinition owner)
    {
        game.ShowNotice(message);
        return false;
    }
}
