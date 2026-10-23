using UnityEngine;
using UnityEngine.UI;

namespace WashedAshore.Interaction
{
    /// <summary>
    /// The single prompt line (N11: the only UI in the feature; tower-td-note §3.5). One screen-space uGUI Text in
    /// the built-in LegacyRuntime font, built here in code so nothing is wired by hand. The Text object is inactive
    /// whenever there is nothing to say, so no Graphic is enabled then.
    /// </summary>
    public class InteractionPrompt : MonoBehaviour
    {
        public const string CanvasName = "InteractionPromptCanvas";
        public const string TextName = "InteractionPromptText";
        const string FontName = "LegacyRuntime.ttf";

        Text text;
        Shadow shadow;
        string shown;
        int sizedForHeight;

        /// <summary>The line on screen right now; null when hidden.</summary>
        public string Shown => shown;
        /// <summary>The one Text; null until first built.</summary>
        public Text Text => text;

        /// <summary>Shows <paramref name="line"/>, or hides the prompt when it is null or empty.</summary>
        public void Show(string line, InteractionSettings tuning)
        {
            if (string.IsNullOrEmpty(line))
            {
                if (shown != null && text != null) text.gameObject.SetActive(false);
                shown = null;
                return;
            }
            if (text == null) Build();
            if (sizedForHeight != Screen.height) Layout(tuning);
            if (!ReferenceEquals(line, shown) && line != shown) text.text = line;
            if (shown == null) text.gameObject.SetActive(true);
            shown = line;
        }

        void Build()
        {
            var canvasGo = new GameObject(CanvasName);
            canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = gameObject.layer; // built after load: stays on the owner's layer (the Player's, see PlayerController.ApplyPlayerLayer)
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var textGo = new GameObject(TextName);
            textGo.transform.SetParent(canvasGo.transform, false);
            textGo.layer = gameObject.layer;
            textGo.SetActive(false);
            text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>(FontName);
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = false;
            shadow = textGo.AddComponent<Shadow>();
        }

        /// <summary>Centred, baseline at the tuned height, cap height a tuned fraction of the screen.</summary>
        void Layout(InteractionSettings tuning)
        {
            sizedForHeight = Screen.height;
            text.fontSize = InteractionRules.FontSize(Screen.height, tuning.promptCapFraction, tuning.promptCapToEm);
            text.color = tuning.promptColor;
            shadow.effectColor = tuning.promptShadowColor;
            shadow.effectDistance = tuning.promptShadowOffset;
            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, tuning.promptHeightFraction);
            rect.anchorMax = new Vector2(1f, tuning.promptHeightFraction);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }
}
