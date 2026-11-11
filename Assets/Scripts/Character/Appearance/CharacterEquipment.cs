using System;
using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.Character.Appearance
{
    /// <summary>What <see cref="CharacterEquipment.Attach"/> returns; pass it to <see cref="CharacterEquipment.Detach"/>.</summary>
    public sealed class AttachHandle
    {
        public CharacterSocketId Id { get; }
        public GameObject Prefab { get; }
        public SocketVisibility Visibility { get; }
        public bool Active { get; internal set; }
        internal GameObject shadowItem, viewItem;

        internal AttachHandle(CharacterSocketId id, GameObject prefab, SocketVisibility visibility)
        {
            Id = id; Prefab = prefab; Visibility = visibility; Active = true;
        }
    }

    /// <summary>
    /// Items on the player's sockets (td-49 B4-B6). One item per socket. The item always goes on the SHADOW body (which casts the
    /// shadow); FirstPersonVisible adds a second copy on the VIEW body. Head items are shadow-only by rule. An attached item is
    /// inert: colliders disabled, rigidbodies removed, on the body's layer, so it never pushes the controller or moves the camera.
    /// Raises <see cref="Changed"/> after every attach, detach and rebind so the first-person body can rescan its renderers.
    /// Not in 49: pickup, equip UI, inventory, saving.
    /// </summary>
    public sealed class CharacterEquipment : MonoBehaviour
    {
        CharacterBody shadow;
        CharacterViewCopy view;
        readonly List<AttachHandle> handles = new List<AttachHandle>();

        public event Action Changed;

        public IReadOnlyList<AttachHandle> Handles => handles;

        /// <summary>Points the equipment at a (new) pair of bodies and re-makes every attached item on them, from the stored (id, prefab, visibility) list. Assumes the previous bodies (and the items on them) are already gone.</summary>
        public void Bind(CharacterBody shadowBody, CharacterViewCopy viewCopy)
        {
            shadow = shadowBody;
            view = viewCopy;
            foreach (var h in handles) Make(h);
            Changed?.Invoke();
        }

        /// <summary>The item on the shadow body at <paramref name="id"/>, or null.</summary>
        public GameObject Occupant(CharacterSocketId id)
        {
            foreach (var h in handles) if (h.Id == id) return h.shadowItem;
            return null;
        }

        /// <summary>The first-person copy at <paramref name="id"/>, or null (none for ShadowOnly items).</summary>
        public GameObject ViewOccupant(CharacterSocketId id)
        {
            foreach (var h in handles) if (h.Id == id) return h.viewItem;
            return null;
        }

        /// <summary>Attaches <paramref name="prefab"/> to the socket. Returns null (and logs an error) for a null prefab, no bodies, an occupied socket, or FirstPersonVisible on Head.</summary>
        public AttachHandle Attach(CharacterSocketId id, GameObject prefab, SocketVisibility visibility)
        {
            if (prefab == null) { Debug.LogError($"CharacterEquipment: attach to {id}: prefab is null"); return null; }
            if (shadow == null || shadow.Sockets == null) { Debug.LogError($"CharacterEquipment: attach to {id}: the character has no body"); return null; }
            if (id == CharacterSocketId.Head && visibility == SocketVisibility.FirstPersonVisible)
            {
                Debug.LogError("CharacterEquipment: the Head socket is shadow-only (the first-person body hides every head-worn item)");
                return null;
            }
            foreach (var h in handles)
                if (h.Id == id) { Debug.LogError($"CharacterEquipment: socket {id} is occupied by '{h.Prefab.name}'"); return null; }
            var handle = new AttachHandle(id, prefab, visibility);
            handles.Add(handle);
            Make(handle);
            Changed?.Invoke();
            return handle;
        }

        /// <summary>Removes the item from both bodies. False when the handle is not active.</summary>
        public bool Detach(AttachHandle handle)
        {
            if (handle == null || !handle.Active || !handles.Remove(handle)) return false;
            handle.Active = false;
            Remove(handle);
            Changed?.Invoke();
            return true;
        }

        void Make(AttachHandle h)
        {
            h.shadowItem = h.viewItem = null;
            if (shadow == null || shadow.Sockets == null) return;
            h.shadowItem = Spawn(h.Prefab, shadow.Sockets.Get(h.Id), shadow.gameObject.layer);
            if (h.Visibility == SocketVisibility.FirstPersonVisible && view != null && view.Body != null && view.Body.Sockets != null)
                h.viewItem = Spawn(h.Prefab, view.Body.Sockets.Get(h.Id), view.Body.gameObject.layer);
        }

        static void Remove(AttachHandle h)
        {
            Discard(h.shadowItem);
            Discard(h.viewItem);
            h.shadowItem = h.viewItem = null;
        }

        static void Discard(GameObject item)
        {
            if (item == null) return;
            item.transform.SetParent(null, false); // out of both body roots at once, so a rescan this frame no longer sees it
            if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
        }

        static GameObject Spawn(GameObject prefab, Transform socket, int layer)
        {
            if (socket == null) return null;
            var item = Instantiate(prefab, socket, false);
            foreach (var c in item.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in item.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.isKinematic = true;
                rb.detectCollisions = false;
                if (Application.isPlaying) Destroy(rb); else DestroyImmediate(rb);
            }
            SetLayerRecursive(item.transform, layer);
            return item;
        }

        static void SetLayerRecursive(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayerRecursive(child, layer);
        }
    }
}
