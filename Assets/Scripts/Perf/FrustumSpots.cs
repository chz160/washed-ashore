using System.Collections.Generic;

namespace WashedAshore.Perf
{
    /// <summary>
    /// The saved camera poses for the #9/#10/#11 measurement protocol (issue #9, "Fixed spots"). A pose is a ground
    /// point (x, z in world metres), a compass yaw (0 = north, +Z; 90 = east, +X), a pitch (positive looks down, as
    /// Unity's Euler x) and the eye height above the terrain under the point. The eye's world y is resolved at run time
    /// from the terrain, so a pose stays valid when the ground is rebuilt. The sixth protocol spot, the route, is
    /// BellsBendFpsWalk's -bbFpsWalk walk (FrustumProbe samples its counters).
    /// Override any pose for one run with -fcPose name=x,z,yaw,pitch[,height]; the JSON records each pose's source.
    /// </summary>
    public static class FrustumSpots
    {
        public const float Eye = 1.7f;   // BellsBendFpsWalk.Eye: the walk's eye height

        // Sources. Landmarks: Data/terrain/build/map_vectors.json "landmarks"; the site offset of LookoutTower is added as
        // farview_matrix.ps1 does. ridge_top and river_bank are the pose-check winners (#25 note_technical-director_pose_winners;
        // technical-artist's verdict on run 20261112-1029-posecheck): candidates fixed beforehand in
        // TestResults/9-ridge-candidates.json (RidgeCandidates.cs) and TestResults/9-shore-candidates.json (ShoreCandidates.cs).
        const float ParkX = -202.8f, ParkZ = -942.1f;                 // PlayerSpawn marker, issue #9
        const float OutdoorCentreX = -145.88f, OutdoorCentreZ = -852.69f;
        const float BankX = 1027.2f, BankZ = 13.9f, BankNextX = 1010.6f, BankNextZ = 34.5f;  // W10 Shore[3], facing Shore[4]
        const float PondsX = -826.13f, PondsZ = 849.41f;              // WestBankPonds: the west bottomland
        const float McCordX = 401.36f, McCordZ = 717.27f;
        const float SiteX = 546.15f + 1.85f, SiteZ = -230.94f + 0.94f; // LookoutTower x,z + site offset
        const float RidgeX = 568f, RidgeZ = -180f, RidgeYaw = 45f;     // ridge_c1: the ridge candidate that passed

        public const string Park = "park", RiverBank = "river_bank", ValleyFloor = "valley_floor", RidgeTop = "ridge_top", Cab = "cab";
        public static readonly string[] StaticSpots = { Park, RiverBank, ValleyFloor, RidgeTop, Cab };

        /// <summary>The default poses. <paramref name="cabHeight"/> is the cab eye height above the summit ground.</summary>
        public static List<SpotPose> Defaults(float cabHeight)
        {
            return new List<SpotPose>
            {
                new SpotPose(Park, ParkX, ParkZ, FrustumMath.YawToward(ParkX, ParkZ, OutdoorCentreX, OutdoorCentreZ), 0f, Eye,
                    "PlayerSpawn (-202.8, -942.1), issue #9; facing LM_OutdoorCenter (map_vectors OutdoorCenter)"),
                new SpotPose(RiverBank, BankX, BankZ, FrustumMath.YawToward(BankX, BankZ, BankNextX, BankNextZ), 0f, Eye,
                    "river_c3: W10 shore-route station Shore[3] (BellsBendFpsWalk.Shore), facing Shore[4] along the bank; pose-check winner (the first station's pose stood at a trunk)"),
                new SpotPose(ValleyFloor, PondsX, PondsZ, FrustumMath.YawToward(PondsX, PondsZ, McCordX, McCordZ), 0f, Eye,
                    "WestBankPonds landmark (west bottomland, map_vectors); facing McCord Bluff across the valley"),
                new SpotPose(RidgeTop, RidgeX, RidgeZ, RidgeYaw, 0f, Eye,
                    "ridge_c1: RidgeCandidates rank 1, 10 m grid within 150 m of the LookoutTower site, ground within 15 m of the site ground, terrain sightline past 600 m, fewest trees on the ray; pose-check winner (the earlier pose 32 m west of the site saw only forest)"),
                new SpotPose(Cab, SiteX, SiteZ, FrustumMath.YawToward(SiteX, SiteZ, ParkX, ParkZ), 0f, cabHeight,
                    "LookoutTower site (map_vectors x,z + site offset), cab eye height from -fcCabHeight; facing the park"),
            };
        }

        /// <summary>Applies -fcPose overrides by name (a new name adds a spot).</summary>
        public static List<SpotPose> WithOverrides(List<SpotPose> poses, IEnumerable<SpotPose> overrides)
        {
            var result = new List<SpotPose>(poses);
            foreach (var o in overrides)
            {
                int i = result.FindIndex(p => p.name == o.name);
                if (i >= 0) result[i] = o; else result.Add(o);
            }
            return result;
        }
    }
}
