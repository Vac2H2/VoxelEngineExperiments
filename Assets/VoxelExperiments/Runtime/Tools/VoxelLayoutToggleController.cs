using UnityEngine;
using UnityEngine.InputSystem;

namespace VoxelExperiments.Runtime.Tools
{
    [DisallowMultipleComponent]
    public sealed class VoxelLayoutToggleController : MonoBehaviour
    {
        [SerializeField] private GameObject _linearRoot;
        [SerializeField] private GameObject _octantRoot;
        [SerializeField] private bool _startWithLinear = true;
        [SerializeField] private Vector2 _screenMargin = new Vector2(16f, 16f);
        [SerializeField] private int _fontSize = 18;

        private GUIStyle _labelStyle;

        private bool IsLinearActive => _linearRoot != null && _linearRoot.activeSelf;

        private void Awake()
        {
            ApplyRequestedState(ResolveInitialLinearState());
        }

        private void OnValidate()
        {
            if (_fontSize < 1)
            {
                _fontSize = 1;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                ApplyRequestedState(!IsLinearActive);
            }

        }

        private void OnGUI()
        {
            EnsureLabelStyle();

            string label = $"Voxel Layout: {(IsLinearActive ? "Linear" : "Octant")}";
            Vector2 size = _labelStyle.CalcSize(new GUIContent(label));
            float x = _screenMargin.x;
            float y = Screen.height - _screenMargin.y - size.y;
            GUI.Label(new Rect(x, y, size.x, size.y), label, _labelStyle);
        }

        private bool ResolveInitialLinearState()
        {
            if (_linearRoot != null && _octantRoot != null)
            {
                if (_linearRoot.activeSelf && !_octantRoot.activeSelf)
                {
                    return true;
                }

                if (_octantRoot.activeSelf && !_linearRoot.activeSelf)
                {
                    return false;
                }
            }

            return _startWithLinear;
        }

        private void ApplyRequestedState(bool enableLinear)
        {
            if (_linearRoot != null)
            {
                _linearRoot.SetActive(enableLinear);
            }

            if (_octantRoot != null)
            {
                _octantRoot.SetActive(!enableLinear);
            }
        }

        private void EnsureLabelStyle()
        {
            if (_labelStyle != null && _labelStyle.fontSize == _fontSize)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = _fontSize,
                normal = { textColor = Color.white }
            };
        }
    }
}
