using System;
using MagicMike.ModSettings;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VillagePlebs
{
    internal sealed class HsvPicker
    {
        private const float HueWidth = 20f;

        private const float Gap = 8f;

        private const int ShadeSize = 32;

        private const int HueSteps = 64;

        public Action<float, float, float> OnChanged;

        private readonly RawImage _shades;

        private readonly RectTransform _shadeMarker;

        private readonly Image _shadeMarkerFill;

        private readonly RectTransform _hueMarker;

        private Texture2D _shadeTexture;

        private Texture2D _hueTexture;

        private float _shadesHue = -1f;

        private float _hue;

        private float _saturation;

        private float _value;

        public HsvPicker(Transform parent, float height)
        {
            GameObject gameObject = Widgets.Child(parent, "ColourPicker");
            LayoutElement element = gameObject.AddComponent<LayoutElement>();
            element.minHeight = height;
            element.preferredHeight = height;
            Image shadesFrame = Widgets.Box(gameObject.transform, "Shades", Theme.Field, Theme.FieldOutline);
            Widgets.Place(shadesFrame.gameObject, Vector2.zero, Vector2.one, Vector2.zero, new Vector2(0f - (HueWidth + Gap), 0f));
            _shades = Picture(shadesFrame.transform, ShadeTexture(), delegate(Vector2 at)
            {
                Pick(_hue, at.x, at.y);
            });
            _shadeMarker = (RectTransform)Widgets.Box(_shades.transform, "Marker", Color.white, Color.black, 1.5f).transform;
            _shadeMarker.sizeDelta = new Vector2(12f, 12f);
            _shadeMarker.GetComponent<Image>().raycastTarget = false;
            _shadeMarkerFill = Widgets.Box(_shadeMarker, "Fill", Color.white);
            Widgets.Fill(_shadeMarkerFill.gameObject, 2f);
            _shadeMarkerFill.raycastTarget = false;
            Image huesFrame = Widgets.Box(gameObject.transform, "Hues", Theme.Field, Theme.FieldOutline);
            Widgets.Place(huesFrame.gameObject, new Vector2(1f, 0f), Vector2.one, new Vector2(0f - HueWidth, 0f), Vector2.zero);
            RawImage hues = Picture(huesFrame.transform, HueTexture(), delegate(Vector2 at)
            {
                Pick(at.y, _saturation, _value);
            });
            Image hueMarker = Widgets.Box(hues.transform, "Marker", Color.white, Color.black, 1.5f);
            Widgets.Place(hueMarker.gameObject, Vector2.zero, new Vector2(1f, 0f), new Vector2(-3f, -2f), new Vector2(3f, 2f));
            hueMarker.raycastTarget = false;
            _hueMarker = (RectTransform)hueMarker.transform;
        }

        public void Set(float hue, float saturation, float value)
        {
            _hue = Mathf.Clamp01(hue);
            _saturation = Mathf.Clamp01(saturation);
            _value = Mathf.Clamp01(value);
            Refresh();
        }

        public void Destroy()
        {
            if (_shadeTexture != null)
            {
                UnityEngine.Object.Destroy(_shadeTexture);
                _shadeTexture = null;
            }
            if (_hueTexture != null)
            {
                UnityEngine.Object.Destroy(_hueTexture);
                _hueTexture = null;
            }
        }

        private void Pick(float hue, float saturation, float value)
        {
            Set(hue, saturation, value);
            OnChanged?.Invoke(_hue, _saturation, _value);
        }

        private void Refresh()
        {
            if (_shades != null && _shadesHue != _hue)
            {
                _shades.texture = ShadeTexture();
            }
            if (_shadeMarker != null)
            {
                _shadeMarker.anchorMin = _shadeMarker.anchorMax = new Vector2(_saturation, _value);
                _shadeMarker.anchoredPosition = Vector2.zero;
            }
            if (_shadeMarkerFill != null)
            {
                _shadeMarkerFill.color = Color.HSVToRGB(_hue, _saturation, _value);
            }
            if (_hueMarker != null)
            {
                _hueMarker.anchorMin = new Vector2(0f, _hue);
                _hueMarker.anchorMax = new Vector2(1f, _hue);
            }
        }

        private static RawImage Picture(Transform frame, Texture texture, Action<Vector2> onPick)
        {
            GameObject gameObject = Widgets.Child(frame, "Picture");
            Widgets.Fill(gameObject, 1f);
            RawImage rawImage = gameObject.AddComponent<RawImage>();
            rawImage.texture = texture;
            gameObject.AddComponent<PickArea>().OnPick = onPick;
            return rawImage;
        }

        private Texture2D ShadeTexture()
        {
            if (_shadeTexture == null)
            {
                _shadeTexture = new Texture2D(ShadeSize, ShadeSize, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "PW hair shades",
                    wrapMode = TextureWrapMode.Clamp
                };
            }
            _shadesHue = _hue;
            Color[] pixels = new Color[ShadeSize * ShadeSize];
            for (int i = 0; i < ShadeSize; i++)
            {
                for (int j = 0; j < ShadeSize; j++)
                {
                    pixels[i * ShadeSize + j] = Color.HSVToRGB(_shadesHue, j / (float)(ShadeSize - 1), i / (float)(ShadeSize - 1));
                }
            }
            _shadeTexture.SetPixels(pixels);
            _shadeTexture.Apply(updateMipmaps: false);
            return _shadeTexture;
        }

        private Texture2D HueTexture()
        {
            if (_hueTexture == null)
            {
                _hueTexture = new Texture2D(1, HueSteps, TextureFormat.RGBA32, mipChain: false)
                {
                    name = "PW hair hues",
                    wrapMode = TextureWrapMode.Clamp
                };
                for (int i = 0; i < HueSteps; i++)
                {
                    _hueTexture.SetPixel(0, i, Color.HSVToRGB(i / (float)(HueSteps - 1), 1f, 1f));
                }
                _hueTexture.Apply(updateMipmaps: false);
            }
            return _hueTexture;
        }

        private sealed class PickArea : MonoBehaviour, IPointerDownHandler, IDragHandler
        {
            public Action<Vector2> OnPick;

            public void OnPointerDown(PointerEventData eventData)
            {
                Pick(eventData);
            }

            public void OnDrag(PointerEventData eventData)
            {
                Pick(eventData);
            }

            private void Pick(PointerEventData eventData)
            {
                RectTransform rectTransform = (RectTransform)base.transform;
                if (eventData.button == PointerEventData.InputButton.Left && RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
                {
                    Rect rect = rectTransform.rect;
                    OnPick?.Invoke(new Vector2(Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width), Mathf.Clamp01((localPoint.y - rect.yMin) / rect.height)));
                }
            }
        }
    }
}
