using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
#endif

namespace Stickman
{
    // Keyboard input for both the Input System and the legacy Input Manager.
    // With the Input System, keys are QWERTY positions.
    public static class StickmanInput
    {
        public static bool IsPressed(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && TryConvert(keyCode, out Key key) && keyboard[key].isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(keyCode);
#else
            return false;
#endif
        }

        public static bool WasPressedThisFrame(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && TryConvert(keyCode, out Key key) && keyboard[key].wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(keyCode);
#else
            return false;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        static readonly Dictionary<KeyCode, Key> KeyCache = new Dictionary<KeyCode, Key>();

        // Maps a KeyCode to an Input System key by name (cached).
        static bool TryConvert(KeyCode keyCode, out Key key)
        {
            if (!KeyCache.TryGetValue(keyCode, out key))
            {
                if (!Enum.TryParse(keyCode.ToString(), out key)) key = Key.None;
                KeyCache[keyCode] = key;
            }
            return key != Key.None;
        }
#endif
    }
}
