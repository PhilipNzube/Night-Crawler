using UnityEngine;

/// <summary>
/// Legacy wrapper for backwards compatibility with existing scene GameObjects.
/// Inherits all functionality from ContextInteractionHUD (looting corpses & healing wounded teammates).
/// </summary>
[System.Obsolete("Renamed to ContextInteractionHUD. Please use ContextInteractionHUD for new components.")]
public class CorpseInteractionHUD : ContextInteractionHUD
{
}
