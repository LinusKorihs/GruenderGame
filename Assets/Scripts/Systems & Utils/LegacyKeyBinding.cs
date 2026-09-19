using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Keeps existing serialized KeyCode bindings while reading from the new Input System.
public static class LegacyKeyBinding
{
    public static bool WasPressedThisFrame(KeyCode binding)
    {
        int code = (int)binding;
        if (binding == KeyCode.JoystickButton0)
        {
            for (int i = 0; i < Gamepad.all.Count; i++)
            {
                if (Gamepad.all[i].buttonSouth.wasPressedThisFrame)
                    return true;
            }

            return false;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return false;

        Key key;
        if (code >= (int)KeyCode.Alpha0 && code <= (int)KeyCode.Alpha9)
            key = (Key)((int)Key.Digit0 + code - (int)KeyCode.Alpha0);
        else if (binding == KeyCode.Return)
            key = Key.Enter;
        else if (!Enum.TryParse(binding.ToString(), true, out key))
            return false;

        return keyboard[key].wasPressedThisFrame;
    }
}
