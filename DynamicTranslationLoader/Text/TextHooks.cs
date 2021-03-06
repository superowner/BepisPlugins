﻿using System.Linq;
using System.Text;
using BepInEx.Logging;
using Harmony;
using TMPro;
using Logger = BepInEx.Logger;
using UnityEngine;

namespace DynamicTranslationLoader.Text
{
    public static class TextHooks
    {
        public static void InstallHooks()
        {
            try
            {
                var harmony = HarmonyInstance.Create("com.bepis.bepinex.dynamictranslationloader");
                harmony.PatchAll(typeof(TextHooks));
            }
            catch (System.Exception e)
            {
                Logger.Log(LogLevel.Error, e);
            }
        }

        public static bool TranslationHooksEnabled { get; set; } = true;

        #region Initialization Hooks
        // With these hooks, we do not need the sceneLoaded event to translate texts

        [HarmonyPostfix, HarmonyPatch(typeof(TextMeshProUGUI), "Awake")]
        public static void AwakeHook(TextMeshProUGUI __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    var newText = TextTranslator.TranslateText(__instance.text, __instance);
                    if (newText != null)
                    {
                        __instance.text = newText;
                    }
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(TextMeshPro), "Awake")]
        public static void AwakeHook(TextMeshPro __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    var newText = TextTranslator.TranslateText(__instance.text, __instance);
                    if (newText != null)
                    {
                        __instance.text = newText;
                    }
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UnityEngine.UI.Text), "OnEnable")]
        public static void OnEnableHook(UnityEngine.UI.Text __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    var newText = TextTranslator.TranslateText(__instance.text, __instance);
                    if (newText != null)
                    {
                        __instance.text = newText;
                    }
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        #endregion

        #region Text Change Hooks

        // NOTE: Splitting these two hooks AWAY from eachother fixed the primary problem
        // I do not think it is allowed to patch two classes in the same method...
        [HarmonyPrefix]
        [HarmonyPatch(typeof(UnityEngine.UI.Text))]
        [HarmonyPatch("text", PropertyMethod.Setter)]
        public static void TextPropertyHook1(ref string value, object __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    value = TextTranslator.TranslateText(value, __instance);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(TMP_Text))]
        [HarmonyPatch("text", PropertyMethod.Setter)]
        public static void TextPropertyHook2(ref string value, object __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    value = TextTranslator.TranslateText(value, __instance);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }
        #endregion

        #region GUI Text Hooks
        [HarmonyPrefix, HarmonyPatch(typeof(TMP_Text), "SetText", new[] { typeof(string), typeof(bool) })]
        public static void SetTextHook1(ref string text, object __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    text = TextTranslator.TranslateText(text, __instance);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TMP_Text), "SetText", new[] { typeof(StringBuilder) })]
        public static void SetTextHook2(ref StringBuilder text, object __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    text = new StringBuilder(TextTranslator.TranslateText(text.ToString(), __instance));
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix, HarmonyPatch(typeof(TMP_Text), "SetText", new[] { typeof(string), typeof(float), typeof(float), typeof(float) })]
        public static void SetTextHook3(ref string text, object __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    text = TextTranslator.TranslateText(text, __instance);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GUI), "DoLabel")]
        public static void DoLabel(GUIContent content, object __instance)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    content.text = TextTranslator.TranslateTextAlternate(content.text);
                    content.tooltip = TextTranslator.TranslateTextAlternate(content.tooltip);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GUI), "DoButton")]
        public static void DoButton(GUIContent content)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    content.text = TextTranslator.TranslateTextAlternate(content.text);
                    content.tooltip = TextTranslator.TranslateTextAlternate(content.tooltip);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GUI), "DoToggle")]
        public static void DoToggle(GUIContent content)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    content.text = TextTranslator.TranslateTextAlternate(content.text);
                    content.tooltip = TextTranslator.TranslateTextAlternate(content.tooltip);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GUI), "DoWindow")]
        public static void DoWindow(GUIContent title)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    title.text = TextTranslator.TranslateTextAlternate(title.text);
                    title.tooltip = TextTranslator.TranslateTextAlternate(title.tooltip);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GUI), "DoButtonGrid")]
        public static void DoButtonGrid(GUIContent[] contents)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    foreach (GUIContent content in contents)
                    {
                        content.text = TextTranslator.TranslateTextAlternate(content.text);
                        content.tooltip = TextTranslator.TranslateTextAlternate(content.tooltip);
                    }
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(GUI), "DoTextField", new[] { typeof(Rect), typeof(int), typeof(GUIContent), typeof(bool), typeof(int), typeof(GUIStyle), typeof(string), typeof(char) })]
        public static void DoTextField(GUIContent content)
        {
            if (TranslationHooksEnabled)
            {
                TranslationHooksEnabled = false;
                try
                {
                    content.text = TextTranslator.TranslateTextAlternate(content.text);
                    content.tooltip = TextTranslator.TranslateTextAlternate(content.tooltip);
                }
                finally
                {
                    TranslationHooksEnabled = true;
                }
            }
        }
        #endregion

        #region Hooks, I think are not needed for KK
        //// There's also 3 SetCharArray methods that should be hooked, but they are kinda annoying to implement with prefix
        //// Luckily, I do not think they are used in KK
        //[HarmonyPostfix, HarmonyPatch( typeof( TMP_Text ), "SetCharArray", new[] { typeof( char[] ) } )]
        //public static void SetCharArray1( TMP_Text __instance )
        //{
        //   if( TranslationHooksEnabled )
        //   {
        //      TranslationHooksEnabled = false;
        //      try
        //      {
        //         var newText = DynamicTranslator.Translate( __instance.text, __instance );
        //         if( newText != null )
        //         {
        //            __instance.text = newText;
        //         }
        //      }
        //      finally
        //      {
        //         TranslationHooksEnabled = true;
        //      }
        //   }
        //}

        //[HarmonyPostfix, HarmonyPatch( typeof( TMP_Text ), "SetCharArray", new[] { typeof( char[] ), typeof( int ), typeof( int ) } )]
        //public static void SetCharArray2( TMP_Text __instance )
        //{
        //   if( TranslationHooksEnabled )
        //   {
        //      TranslationHooksEnabled = false;
        //      try
        //      {
        //         var newText = DynamicTranslator.Translate( __instance.text, __instance );
        //         if( newText != null )
        //         {
        //            __instance.text = newText;
        //         }
        //      }
        //      finally
        //      {
        //         TranslationHooksEnabled = true;
        //      }
        //   }
        //}

        //[HarmonyPostfix, HarmonyPatch( typeof( TMP_Text ), "SetCharArray", new[] { typeof( int[] ), typeof( int ), typeof( int ) } )]
        //public static void SetCharArray3( TMP_Text __instance )
        //{
        //   if( TranslationHooksEnabled )
        //   {
        //      TranslationHooksEnabled = false;
        //      try
        //      {
        //         var newText = DynamicTranslator.Translate( __instance.text, __instance );
        //         if( newText != null )
        //         {
        //            __instance.text = newText;
        //         }
        //      }
        //      finally
        //      {
        //         TranslationHooksEnabled = true;
        //      }
        //   }
        //}
        #endregion

        #region Text break hooks
        [HarmonyPrefix, HarmonyPatch(typeof(HyphenationJpn), "IsLatin")]
        public static bool UpdateText(ref bool __result, ref char s)
        {
            // Break only on space?
            __result = s != ' ';
            return false;
        }
        [HarmonyPostfix, HarmonyPatch(typeof(HyphenationJpn), "GetFormatedText")]
        public static void GetFormatedText(ref string __result)
        {
            // When the width of the text is greater than its container, a space is inserted.
            // This can throw off our formatting, so we remove all occurrences of it.

            __result = __result.Replace("\u3000", "");
            __result = string.Join("\n", __result.Split('\n').Select(x => x.Trim()).ToArray());
        }
        #endregion
    }
}