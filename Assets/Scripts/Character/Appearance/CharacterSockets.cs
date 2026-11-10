using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    /// <summary>Named attach points on a character (td-49, adr/49-phase2-reskin-and-sockets part B). Fixed small set, so an enum.</summary>
    public enum CharacterSocketId { Back, HandR, HandL, Head, Hips }

    /// <summary>Whether an attached item is also drawn by the first-person camera. ShadowOnly: only the shadow body carries it.</summary>
    public enum SocketVisibility { ShadowOnly, FirstPersonVisible }

    /// <summary>
    /// Where each socket sits: a parent bone and a local pose. One asset per skeleton type, or null on the appearance for the defaults.
    /// Offsets are tuned by the technical artist; the defaults are zero offsets on the named bones (source: td-49 part B3, not measured).
    /// </summary>
    [CreateAssetMenu(menuName = "Washed Ashore/Character Socket Set", fileName = "CharacterSocketSet")]
    public sealed class CharacterSocketSet : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public CharacterSocketId id;
            [Tooltip("Name of the bone the socket is a child of. A missing bone fails assembly with an error.")]
            public string parentBone;
            public Vector3 localPosition;
            public Vector3 localEuler;
        }

        public List<Entry> entries = new List<Entry>();

        /// <summary>The default parents (td-49 B3): Back = Chest, HandR = Wrist.R, HandL = Wrist.L, Head = Head, Hips = Hips; zero offsets.</summary>
        public static Entry Default(CharacterSocketId id)
        {
            switch (id)
            {
                case CharacterSocketId.Back: return new Entry { id = id, parentBone = "Chest" };
                case CharacterSocketId.HandR: return new Entry { id = id, parentBone = "Wrist.R" };
                case CharacterSocketId.HandL: return new Entry { id = id, parentBone = "Wrist.L" };
                case CharacterSocketId.Head: return new Entry { id = id, parentBone = "Head" };
                default: return new Entry { id = id, parentBone = "Hips" };
            }
        }

        /// <summary>The entry for <paramref name="id"/> from <paramref name="set"/> (may be null), or the default when the set has none.</summary>
        public static Entry Resolve(CharacterSocketSet set, CharacterSocketId id)
        {
            if (set != null)
                foreach (var e in set.entries)
                    if (e.id == id) return e;
            return Default(id);
        }
    }

    /// <summary>
    /// The sockets of one body (shadow or view). Each is an empty Transform named <c>Socket_&lt;Id&gt;</c>, a child of its parent bone,
    /// made AFTER <see cref="CharacterBody"/> built its bone map, so the bone set (79 men, 62 women) and FitsSkeleton never see it.
    /// Never author sockets into the skeleton prefabs.
    /// </summary>
    public sealed class CharacterSockets : MonoBehaviour
    {
        public const string Prefix = "Socket_";

        readonly Dictionary<CharacterSocketId, Transform> sockets = new Dictionary<CharacterSocketId, Transform>();

        public static string NameOf(CharacterSocketId id) => Prefix + id;

        public Transform Get(CharacterSocketId id) => sockets.TryGetValue(id, out var t) ? t : null;

        /// <summary>True when <paramref name="t"/> is a socket or below one, walking up to (not past) <paramref name="root"/>.</summary>
        public static bool IsUnderSocket(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (t.name.StartsWith(Prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Makes every socket on <paramref name="body"/>. False (with an error naming the bone) when a parent bone is missing.</summary>
        internal static bool Create(CharacterBody body, CharacterSocketSet set)
        {
            var made = body.gameObject.GetComponent<CharacterSockets>() ?? body.gameObject.AddComponent<CharacterSockets>();
            foreach (CharacterSocketId id in Enum.GetValues(typeof(CharacterSocketId)))
            {
                if (made.sockets.ContainsKey(id)) continue;
                var entry = CharacterSocketSet.Resolve(set, id);
                var parent = body.Bone(entry.parentBone);
                if (parent == null)
                {
                    Debug.LogError($"CharacterSockets: socket {id}: parent bone '{entry.parentBone}' is not in the skeleton of '{body.name}'");
                    return false;
                }
                var go = new GameObject(NameOf(id));
                go.layer = body.gameObject.layer;
                var t = go.transform;
                t.SetParent(parent, false);
                t.localPosition = entry.localPosition;
                t.localRotation = Quaternion.Euler(entry.localEuler);
                made.sockets[id] = t;
            }
            body.Sockets = made;
            return true;
        }
    }
}
