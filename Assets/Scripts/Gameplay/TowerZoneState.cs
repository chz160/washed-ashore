namespace WashedAshore.Gameplay
{
    /// <summary>
    /// What TowerWalkZones keeps for one player (#46 P4): the capsule values it saved when it slimmed or raised them, and the zone
    /// readings of the last pass. Lives on the player (<see cref="PlayerController.TowerZone"/>), never on the zone component, so any
    /// number of players can use the tower at once.
    /// </summary>
    public struct TowerZoneState
    {
        public float savedSlope, savedRadius, savedHeight, savedStep;
        public bool slopeRaised, slimmed;
        public bool InFlightZone, OnRamp, InFootprint;
    }
}
