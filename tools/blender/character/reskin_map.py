"""Mapping tables for reskin_survivor.py (Mixamo-rigged Meshy body -> game 79-bone Men skeleton). td-49 phase 2 part A3 is the source.
Swap this table to serve another Mixamo-rigged body; the script itself holds no bone names.
"""
P = "mixamorig:"

# Meshy bone -> game bone that receives its weights. Hand weights go to Wrist, then the fingers take their share by finger_weights().
MESHY_TO_GAME = {
    P + "Hips": "Hips", P + "Spine": "Abdomen", P + "Spine1": "Torso", P + "Spine2": "Chest",
    P + "Neck": "Neck", P + "Head": "Head", P + "HeadTop_End": "Head", "headfront": "Head",
    P + "LeftShoulder": "Shoulder.L", P + "LeftArm": "UpperArm.L", P + "LeftForeArm": "LowerArm.L", P + "LeftHand": "Wrist.L",
    P + "RightShoulder": "Shoulder.R", P + "RightArm": "UpperArm.R", P + "RightForeArm": "LowerArm.R", P + "RightHand": "Wrist.R",
    P + "LeftUpLeg": "UpperLeg.L", P + "LeftLeg": "LowerLeg.L", P + "LeftFoot": "Foot.L", P + "LeftToeBase": "Foot.L",
    P + "RightUpLeg": "UpperLeg.R", P + "RightLeg": "LowerLeg.R", P + "RightFoot": "Foot.R", P + "RightToeBase": "Foot.R",
}

# Meshy bone rotated (swing only) so its direction matches a game direction: (game bone a, game bone b) = b.head - a.head, or (a, "_tail") = a.tail - a.head.
# Spine, neck, head, feet are not rotated: the Meshy A-pose body already stands upright.
LIMB_TARGET = {
    P + "LeftShoulder": ("Shoulder.L", "_tail"), P + "RightShoulder": ("Shoulder.R", "_tail"),
    P + "LeftArm": ("UpperArm.L", "_tail"), P + "RightArm": ("UpperArm.R", "_tail"),
    P + "LeftForeArm": ("LowerArm.L", "_tail"), P + "RightForeArm": ("LowerArm.R", "_tail"),
    P + "LeftHand": ("Wrist.L", "Middle2.L"), P + "RightHand": ("Wrist.R", "Middle2.R"),
    P + "LeftUpLeg": ("UpperLeg.L", "_tail"), P + "RightUpLeg": ("UpperLeg.R", "_tail"),
    P + "LeftLeg": ("LowerLeg.L", "_tail"), P + "RightLeg": ("LowerLeg.R", "_tail"),
}

# Joint-residual table rows: (Meshy bone head, game bone head). Not every pair is the same anatomical joint (the game Foot bone is at the floor).
RESIDUAL_PAIRS = [
    (P + "Hips", "Hips"), (P + "Spine", "Abdomen"), (P + "Spine1", "Torso"), (P + "Spine2", "Chest"), (P + "Neck", "Neck"), (P + "Head", "Head"),
    (P + "LeftShoulder", "Shoulder.L"), (P + "LeftArm", "UpperArm.L"), (P + "LeftForeArm", "LowerArm.L"), (P + "LeftHand", "Wrist.L"),
    (P + "RightShoulder", "Shoulder.R"), (P + "RightArm", "UpperArm.R"), (P + "RightForeArm", "LowerArm.R"), (P + "RightHand", "Wrist.R"),
    (P + "LeftUpLeg", "UpperLeg.L"), (P + "LeftLeg", "LowerLeg.L"), (P + "LeftFoot", "Foot.L"),
    (P + "RightUpLeg", "UpperLeg.R"), (P + "RightLeg", "LowerLeg.R"), (P + "RightFoot", "Foot.R"),
]

FINGER_BONES = ["Thumb1", "Thumb2", "Thumb3", "Index1", "Index2", "Index3", "Index4", "Middle1", "Middle2", "Middle3", "Middle4",
                "Ring1", "Ring2", "Ring3", "Ring4", "Pinky1", "Pinky2", "Pinky3", "Pinky4"]
HEAD_BONES = [P + "Head", P + "HeadTop_End", "headfront"]
SIDE_OF_GAME = {"L": 1.0, "R": -1.0}  # character left = +X
EYE_TARGET_Z = 1.65  # td-49 A2: s = game standing eye 1.65 / Meshy eye
S_BAND = (0.98, 1.04)  # outside this s goes PENDING to td-49 + director-49
WATCH_SIDE = "R"  # team-lead 2026-11-03: the watch is on his RIGHT wrist

# Per-segment fit tables (director P1, a labelled PLACEHOLDER COMPROMISE; td-fit-ruling Addendum 2). A fitted Meshy bone is aimed at the landing
# point and scaled ALONG ITS OWN AXIS by the distance ratio. ("joint", game bone) = that bone's head; ("line", game bone) = point on that bone's line at the
# Meshy ankle height (F4: the boot stays rigid on Foot and the shin above the boot collar takes the stretch). Hands and fingers are never fitted.
# The ratios are RESULTS of these targets, printed every run; P2 (Noah) = run without the "arms" group. Groups are selected with --fit arms,legs.
FIT_GROUPS = {
    "arms": {
        P + "LeftShoulder": ("joint", "UpperArm.L"), P + "LeftArm": ("joint", "LowerArm.L"), P + "LeftForeArm": ("joint", "Wrist.L"),
        P + "RightShoulder": ("joint", "UpperArm.R"), P + "RightArm": ("joint", "LowerArm.R"), P + "RightForeArm": ("joint", "Wrist.R"),
    },
    "legs": {
        P + "LeftUpLeg": ("joint", "LowerLeg.L"), P + "LeftLeg": ("line", "LowerLeg.L"),
        P + "RightUpLeg": ("joint", "LowerLeg.R"), P + "RightLeg": ("line", "LowerLeg.R"),
    },
}
EYE_STAND_CONTROLLER = 1.65  # controller standing eye (adr/player-character-1)
FOOT_DROP = 0.05            # the body sits this far below the player origin
EYE_STAND = EYE_STAND_CONTROLLER + FOOT_DROP  # body-frame eye target 1.70 (td K6); the Survivor appearance carries skeletonScale = EYE_STAND / his eye
EYE_TOL = 0.02
SPINE_MESHY = [P + "Hips", P + "Spine", P + "Spine1", P + "Spine2"]
SPINE_GAME = ["Hips", "Abdomen", "Torso", "Chest"]
ELBOW_WRIST_KNEE = ["LowerArm.L", "Wrist.L", "LowerLeg.L", "LowerArm.R", "Wrist.R", "LowerLeg.R"]  # printed first (director L2)
PALM_FRACTION = 0.4  # interim finger method: the first 40% of the Meshy hand (wrist to fingertips) stays palm on Wrist [my choice, check in close-up stills]

# P4 (Skeleton_Human): the arm and leg bones are rotated onto the template directions; the shoulders and spine stay as modelled.
LIMB_TARGET_P4 = {k: v for k, v in LIMB_TARGET.items() if "Shoulder" not in k}
HEIGHT_BAND = (1.80, 1.86)   # m: report-and-route: outside it the script prints 'route to team-lead for Noah' and carries on (Noah accepted 1.85 m; the band is team-lead's re-ask line)
HEIGHT_STOP = (1.78, 1.90)   # m: the hard stop
