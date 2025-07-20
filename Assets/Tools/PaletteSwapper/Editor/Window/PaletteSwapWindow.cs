#if UNITY_EDITOR
#pragma warning disable 0162 // Unreachable code detected
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;

using Color = UnityEngine.Color;
using Font = UnityEngine.Font;

namespace Uee.PaletteSwapper
{
    public class PaletteSwapWindow : EditorWindow
    {
        #region WINDOW

        public static PaletteSwapWindow Window;

        // EditorPrefs keys for persistent settings
        private const string EDITOR_PREFS_KEY_SOURCE_PATH = "LazyPaletteSwapper_SourcePath";
        private const string EDITOR_PREFS_KEY_IGNORE_PIXELS_WITH_ALPHA = "LazyPaletteSwapper_IgnorePixelsWithAlpha";
        private const string EDITOR_PREFS_KEY_AUTO_SAVE_TEXTURE = "LazyPaletteSwapper_AutoSaveTexture";
        private const string EDITOR_PREFS_KEY_SHOW_ADVANCED_SETTINGS = "LazyPaletteSwapper_ShowAdvancedSettings";
        private const string EDITOR_PREFS_KEY_SHOW_INSTRUCTIONS = "LazyPaletteSwapper_ShowInstructions";
        private const string EDITOR_PREFS_KEY_PALETTE_COLOR_LIMIT = "LazyPaletteSwapper_PaletteColorLimit";
        private const string EDITOR_PREFS_KEY_PREVIEW_HEIGHT = "LazyPaletteSwapper_PreviewHeight";

        // Static settings
        private const bool USE_ASYNC = true; // Always use async operations for better performance
        private const bool ENABLE_DEBUG_LOGS = false; // Toggle debug logging on/off

        // Timing settings to prevent modification time errors
        private const float MIN_TIME_BETWEEN_UPDATES = 1f; // Minimum time in seconds between texture updates
        private static float _lastUpdateTime = 0f; // Track when the last texture update happened

        [MenuItem("Lazy-Jedi/Tools/Lazy Palette Swapper", priority = 400)]
        public static void OpenWindow()
        {
            Window = GetWindow<PaletteSwapWindow>("Lazy Palette Swapper");
            Window.minSize = new Vector2(580, 680);
            Window.Show();
        }

        #endregion

        #region STYLING

        private GUIContent _sourceContent;

        private GUIStyle _titleLabel;
        private GUIStyle _headerLabel;
        private GUIStyle _centeredHelpBoxLabel;
        private GUIStyle _centeredLabel;
        private GUIStyle _lightBlueButton;
        private GUIStyle _lightGreenButton;
        private GUIStyle _lightRedButton;
        private GUIStyle _yellowButton;
        private GUIStyle _desaturatedRedLabel;
        private GUIStyle _boldCenteredLabel;

        #endregion

        #region VARIABLES

        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;

        private Task _getPalettesTask;
        private Task _swapColorsTask;

        private Texture2D _source;
        private string _sourcePath;
        private Texture2D _outputTexture;

        private List<Color32> _sourceColors;
        private int _sourceColorsCount;
        private List<Color32> _fullSourceColors; // Store all source colors without limit
        private int _totalSourceColorsCount; // Total number of colors found in source texture

        private List<Color32> _newColors;
        private int _newColorsCount;

        private int _ignorePixelsWithAlpha = 255;

        private List<List<PixelXY>> _map = new List<List<PixelXY>>();

        private string _outputPath = string.Empty;
        private string _filename = string.Empty;
        private string _extension = string.Empty;
        private string _customPaletteName = string.Empty;
        private List<string> _existingPalettePaths = new List<string>();
        private int _selectedPaletteIndex = 0;

        private LazySwapper _lazySwapper;

        private bool _showAdvancedSettings = false;
        private bool _showInstructions = false;
        private bool _autoSwap = true;
        private bool _pendingAutoSwap = false;
        private float _lastOutputTextureUpdate = 0f;
        private float _outputTextureUpdateRate = 3f; // Update every 3 seconds
        private bool _isNewTexture = false; // Track if this is a new texture that needs import settings
        private int _paletteColorLimit = 16; // Maximum number of colors allowed in a palette
        private bool _showColorLimitWarning = false; // Flag to show color limit warning
        private int _previewHeight = 100; // Preview height in pixels (default 100, range 50-400)

        // HSV Tool settings
        private bool _useHsvTool = false; // Enable HSV color adjustment tool
        private float _hsvHueShift = 0f; // Hue shift (-180 to 180)
        private float _hsvSaturationShift = 0f; // Saturation shift (-100 to 100)
        private float _hsvValueShift = 0f; // Value shift (-100 to 100)

        private Vector2 _scrollPosition;

        #endregion

        #region HELPER PROPERTIES

        private float AvailableWidth => position.width; // Use full width, let Unity handle scroll bar space automatically

        #endregion

        #region UNITY METHODS

        private void OnEnable()
        {
            // Set minimum window size
            minSize = new Vector2(540f, 600f);
            LoadEditorPrefs();
        }

        public void OnGUI()
        {
            // Ensure button styles are initialized
            if (_lightBlueButton == null || _lightGreenButton == null || _yellowButton == null)
            {
                Initialization();
            }

            using (EditorGUILayout.ScrollViewScope scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scrollView.scrollPosition;

                DrawTitle();

                DrawTexturesSection();

                // Only show these sections when source texture is loaded
                if (_source != null)
                {
                    // Only show palette settings when color limit is not reached
                    if (!_showColorLimitWarning)
                    {
                        DrawPalettesSettings();
                    }

                    // Auto saving button (centered, outside any box) - only show when auto save is disabled
                    if (!_autoSwap)
                    {
                        EditorGUILayout.Space(16f);
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        SwapPaletteButton();
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        EditorGUILayout.Space(10f);
                    }
                }
            }
        }

        private void Update()
        {
            // Handle auto-swap
            if (_pendingAutoSwap)
            {
                ExecuteAutoSwap();
            }

            // Repaint the UI when we're showing the waiting timer
            float timeSinceLastUpdate = Time.realtimeSinceStartup - _lastUpdateTime;
            if (timeSinceLastUpdate < MIN_TIME_BETWEEN_UPDATES && _sourceColors != null && !_autoSwap)
            {
                // Only repaint roughly every 0.1 seconds to avoid excessive repaints
                if (Mathf.FloorToInt(timeSinceLastUpdate * 10) != Mathf.FloorToInt((timeSinceLastUpdate - Time.deltaTime) * 10))
                {
                    Repaint();
                }
            }
        }

        private void OnDestroy()
        {
            if (_cancellationTokenSource == null || !USE_ASYNC) return;
            if (_getPalettesTask == null && _swapColorsTask == null) return;
            if ((_getPalettesTask == null || _getPalettesTask.IsCompleted) && (_swapColorsTask == null || _swapColorsTask.IsCompleted)) return;

            CompilationPipeline.RequestScriptCompilation();
            _cancellationTokenSource.Cancel();
            if (ENABLE_DEBUG_LOGS) Debug.Log("Cancelling all Tasks - Requires Script Recompile");
        }

        #endregion

        #region METHODS

        private void DrawTitle()
        {
            EditorGUILayout.Space(11f);
            EditorGUILayout.LabelField("Lazy Palette Swapper", _titleLabel);
            EditorGUILayout.Space(8f);

            // Instructions foldout
            bool showInstructions = EditorGUILayout.Foldout(_showInstructions, "Instructions", true);
            if (showInstructions != _showInstructions)
            {
                _showInstructions = showInstructions;
            }
            if (_showInstructions)
            {
                using (EditorGUILayout.HorizontalScope asd = new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(8f); // Left padding
                    using (EditorGUILayout.VerticalScope instructionsScope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        // EditorGUILayout.LabelField("Basic Usage:", EditorStyles.boldLabel);
                        EditorGUILayout.LabelField("1. Select a source texture to extract its color palette", EditorStyles.label);
                        EditorGUILayout.LabelField("2. Give a name to the palette which will be used when saving (see output path)", EditorStyles.label);
                        EditorGUILayout.LabelField("3. Make changes to the output palette colors", EditorStyles.label);
                        EditorGUILayout.LabelField("4. Auto (default) or manually save texture which will be in the same folder as the source", EditorStyles.label);
                        EditorGUILayout.LabelField("5. Manage multiple palettes under the same source texture from the dropdown", EditorStyles.label);
                        EditorGUILayout.LabelField("6. Palette textures and source texture maintain relationship in the tool via file names", EditorStyles.label);
                        EditorGUILayout.Space(4f);
                    }
                }
            }


            // Advanced settings - always show, regardless of source texture
            EditorGUILayout.Space(4f);
            DrawAdvancedSettings();
        }

        private void DrawTexturesSection()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Textures", _headerLabel);
            EditorGUILayout.Space(8f);
            using (EditorGUILayout.VerticalScope verticalScope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.Space(4f);                // Two horizontal boxes: Source Texture and Output Texture
                using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(10f); // Left padding to align with palette sections

                    // Left: Source Texture
                    using (EditorGUILayout.VerticalScope leftBox = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width((AvailableWidth - 60f) * 0.5f), GUILayout.Height(_previewHeight + 90f)))
                    {
                        EditorGUILayout.LabelField("Source Texture", _boldCenteredLabel);
                        EditorGUILayout.Space(4f);
                        // Texture selection field (centered)
                        GUILayout.BeginHorizontal();
                        GUILayout.FlexibleSpace();
                        EditorGUI.BeginChangeCheck();
                        Texture2D newSource = (Texture2D)EditorGUILayout.ObjectField(_source, typeof(Texture2D), false, GUILayout.Width(120));
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source texture changed from '{_source?.name ?? "null"}' to '{newSource?.name ?? "null"}'");
                            _source = newSource;

                            // Handle source texture change
                            _sourcePath = AssetDatabase.GetAssetPath(_source);
                            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source path set to: {_sourcePath}");

                            if (!string.IsNullOrEmpty(_sourcePath))
                            {
                                // Disable HSV tool when source texture changes
                                _useHsvTool = false;
                                string sourcePathDirectory = Path.GetDirectoryName(_sourcePath);
                                string originalFilename = Path.GetFileNameWithoutExtension(_sourcePath);
                                _extension = $"{Path.GetExtension(_sourcePath)}";
                                if (ENABLE_DEBUG_LOGS) Debug.Log($"Directory: {sourcePathDirectory}, Filename: {originalFilename}, Extension: {_extension}");

                                // Find existing palette textures
                                FindExistingPalettes(sourcePathDirectory, originalFilename);
                                if (ENABLE_DEBUG_LOGS) Debug.Log($"Found {_existingPalettePaths.Count} existing palettes");

                                // Set default palette name based on existing palettes
                                if (_existingPalettePaths.Count == 0)
                                {
                                    _customPaletteName = "New";
                                    if (ENABLE_DEBUG_LOGS) Debug.Log("No existing palettes found, setting custom palette name to 'New'");
                                }
                                else
                                {
                                    _customPaletteName = string.Empty;
                                    // Select the first palette if any exist
                                    _selectedPaletteIndex = 0;
                                    _outputPath = _existingPalettePaths[_selectedPaletteIndex];
                                    _filename = Path.GetFileNameWithoutExtension(_outputPath);
                                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Existing palettes found, selected index 0: {_outputPath}");
                                }

                                // Set default output path with _palette_New suffix
                                _filename = $"{originalFilename}_palette_New";
                                _outputPath = Path.Combine(sourcePathDirectory, $"{_filename}{_extension}");
                                if (ENABLE_DEBUG_LOGS) Debug.Log($"Default output path set to: {_outputPath}");

                                ResetColorLists();
                                if (ENABLE_DEBUG_LOGS) Debug.Log("ResetColorLists() called");

                                // Always get palette when texture is loaded
                                if (_source != null)
                                {
                                    if (ENABLE_DEBUG_LOGS) Debug.Log("Source texture is not null, calling GetPalettes()");
                                    if (USE_ASYNC)
                                    {
                                        if (ENABLE_DEBUG_LOGS) Debug.Log("Using async mode for GetPalettes");
                                        _getPalettesTask = Task.Run(GetPalettes, _cancellationToken);
                                    }
                                    else
                                    {
                                        if (ENABLE_DEBUG_LOGS) Debug.Log("Using sync mode for GetPalettes");
                                        GetPalettes();
                                    }
                                }
                                else
                                {
                                    if (ENABLE_DEBUG_LOGS) Debug.Log("Source texture is null, not calling GetPalettes()");
                                }

                                // Save settings when source texture changes
                                SaveEditorPrefs();
                            }
                            else
                            {
                                // Source texture was set to null, clear everything
                                _sourcePath = string.Empty;
                                _outputPath = string.Empty;
                                _filename = string.Empty;
                                _extension = string.Empty;
                                _existingPalettePaths.Clear();
                                _selectedPaletteIndex = 0;
                                ResetColorLists();
                                SaveEditorPrefs();
                            }
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        // Texture preview (centered, aspect-ratio correct)
                        if (_source != null)
                        {
                            float aspectRatio = (float)_source.width / _source.height;
                            float height = _previewHeight;
                            float width = height * aspectRatio;
                            GUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();
                            Rect previewRect = GUILayoutUtility.GetRect(width, height, GUILayout.MaxWidth(width));
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();
                            // Use point filtering for pixel art preview
                            GUI.DrawTexture(previewRect, _source, ScaleMode.ScaleToFit, true, 0, Color.white, Vector4.zero, Vector4.zero);

                            // Add click functionality to ping the texture
                            if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
                            {
                                EditorGUIUtility.PingObject(_source);
                                Event.current.Use();
                            }
                        }
                        // Center the texture path underneath
                        if (_source)
                        {
                            EditorGUILayout.SelectableLabel(_sourcePath, _centeredLabel);
                        }
                        // Show color limit warning if needed
                        if (_showColorLimitWarning)
                        {
                            EditorGUILayout.Space(8f);
                            EditorGUILayout.HelpBox($"Color limit reached: {_totalSourceColorsCount} colors found, {_paletteColorLimit} limit", MessageType.Warning);
                        }

                        // No flexible space - let box size naturally
                    }
                    GUILayout.Space(8f);
                    // Right: Output Texture
                    using (EditorGUILayout.VerticalScope rightBox = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(_previewHeight + 90f)))
                    {
                        if (_source != null)
                        {
                            EditorGUILayout.LabelField("Output Texture", _boldCenteredLabel);
                            EditorGUILayout.Space(4f);
                            // Output texture field (centered, disabled)
                            GUILayout.BeginHorizontal();
                            GUILayout.FlexibleSpace();
                            EditorGUI.BeginDisabledGroup(true);
                            EditorGUILayout.ObjectField(_outputTexture, typeof(Texture2D), false, GUILayout.Width(120));
                            EditorGUI.EndDisabledGroup();
                            GUILayout.FlexibleSpace();
                            GUILayout.EndHorizontal();

                            // Load output texture if it exists (with rate limiting)
                            if (File.Exists(_outputPath) && Time.realtimeSinceStartup - _lastOutputTextureUpdate >= _outputTextureUpdateRate)
                            {
                                _outputTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(_outputPath);
                                _lastOutputTextureUpdate = Time.realtimeSinceStartup;
                            }
                            else if (!File.Exists(_outputPath))
                            {
                                _outputTexture = null;
                            }

                            // Output texture preview (centered, aspect-ratio correct)
                            if (_outputTexture != null)
                            {
                                float aspectRatio = (float)_outputTexture.width / _outputTexture.height;
                                float height = _previewHeight;
                                float width = height * aspectRatio;
                                GUILayout.BeginHorizontal();
                                GUILayout.FlexibleSpace();
                                Rect previewRect = GUILayoutUtility.GetRect(width, height, GUILayout.MaxWidth(width));
                                GUILayout.FlexibleSpace();
                                GUILayout.EndHorizontal();
                                GUI.DrawTexture(previewRect, _outputTexture, ScaleMode.ScaleToFit, true, 0, Color.white, Vector4.zero, Vector4.zero);

                                // Add click functionality to ping the texture
                                if (Event.current.type == EventType.MouseDown && previewRect.Contains(Event.current.mousePosition))
                                {
                                    EditorGUIUtility.PingObject(_outputTexture);
                                    Event.current.Use();
                                }
                            }
                            else
                            {
                                // Show placeholder text when output texture is null
                                float height = _previewHeight * 0.5f; // Scale with preview height
                                float width = 200f; // Fixed width for placeholder text
                                GUILayout.BeginHorizontal();
                                GUILayout.FlexibleSpace();
                                Rect placeholderRect = GUILayoutUtility.GetRect(width, height, GUILayout.MaxWidth(width));
                                GUILayout.FlexibleSpace();
                                GUILayout.EndHorizontal();

                                // Draw placeholder text
                                GUIStyle placeholderStyle = new GUIStyle(EditorStyles.label)
                                {
                                    alignment = TextAnchor.MiddleCenter,
                                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f) },
                                    fontSize = 10,
                                    wordWrap = false
                                };
                                EditorGUI.LabelField(placeholderRect, "Waiting for first changes to new palette", placeholderStyle);
                            }
                            // Output path display (only show when there's an output texture)
                            if (_outputTexture != null)
                            {
                                EditorGUILayout.Space(7f);
                                EditorGUILayout.LabelField(_outputPath, _centeredLabel);
                            }

                            // No flexible space - let box size naturally
                        }
                        else
                        {
                            // Clear output texture when source is null
                            _outputTexture = null;
                            _outputPath = string.Empty;

                            // Show minimal content when no source texture
                            EditorGUILayout.LabelField("Output Texture", _boldCenteredLabel);
                            EditorGUILayout.Space(4f);
                            EditorGUILayout.LabelField("No source texture selected", _centeredLabel);
                        }
                        // Bottom spacer for output texture group
                    }
                    GUILayout.Space(10f); // Right padding to balance left padding
                }

                // Preview height setting directly under textures (left-aligned)
                EditorGUILayout.Space(8f);
                using (EditorGUILayout.HorizontalScope previewHeightScope = new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.Space(8f);
                    Rect sliderRect = GUILayoutUtility.GetRect(50, 16);
                    float newPreviewHeight = GUI.HorizontalSlider(sliderRect, _previewHeight, 50, 400);
                    if (Mathf.RoundToInt(newPreviewHeight) != _previewHeight)
                    {
                        _previewHeight = Mathf.RoundToInt(newPreviewHeight);
                        SaveEditorPrefs();
                    }
                    GUILayout.FlexibleSpace();
                }
                EditorGUILayout.Space(10f);
            }
        }

        private void DrawPalettesSettings()
        {
            // Ensure button styles are initialized
            if (_lightBlueButton == null || _lightGreenButton == null || _yellowButton == null)
            {
                Initialization();
            }

            EditorGUILayout.Space(16f);
            EditorGUILayout.LabelField("Palette Settings", _headerLabel);

            EditorGUILayout.Space(4f);
            using (EditorGUILayout.VerticalScope verticalScope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                // Current Palette section
                {
                    EditorGUILayout.Space(4f);

                    // Two horizontal boxes: New Palette and Current Palette
                    using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(10f); // Left padding

                        // Left box: New Palette
                        using (EditorGUILayout.VerticalScope leftBox = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width((AvailableWidth - 60f) * 0.5f)))
                        {
                            // Top row: New Palette label and name field
                            using (EditorGUILayout.HorizontalScope topRow = new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.LabelField("New Palette", GUILayout.Width(80));
                                string newCustomName = EditorGUILayout.TextField(_customPaletteName);

                                // Update the custom palette name when text changes
                                if (newCustomName != _customPaletteName)
                                {
                                    _customPaletteName = newCustomName;
                                }
                            }

                            EditorGUILayout.Space(4f);

                            // Bottom row: Buttons
                            using (EditorGUILayout.HorizontalScope buttonRow = new EditorGUILayout.HorizontalScope())
                            {
                                // Check if palette name is valid
                                bool isPaletteNameValid = !string.IsNullOrEmpty(_customPaletteName) && !IsPaletteNameTaken(_customPaletteName);
                                string baseTooltipText = string.IsNullOrEmpty(_customPaletteName) ? "Palette name cannot be empty" :
                                                       IsPaletteNameTaken(_customPaletteName) ? $"Palette '{_customPaletteName}' already exists" : "";

                                // New Palette from Source button
                                string sourceTooltip = string.IsNullOrEmpty(baseTooltipText) ?
                                    "Create a new palette using colors from the source texture" : baseTooltipText;
                                EditorGUI.BeginDisabledGroup(!isPaletteNameValid);
                                if (GUILayout.Button(new GUIContent("New from Source", sourceTooltip)))
                                {
                                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Creating new palette from source: {_customPaletteName}");
                                    _ = Task.Run(async () => await CreateNewPalette(_customPaletteName, true));
                                }
                                EditorGUI.EndDisabledGroup();

                                EditorGUILayout.Space(4f);

                                // New Palette from Current button
                                string currentTooltip = string.IsNullOrEmpty(baseTooltipText) ?
                                    "Create a new palette using colors from the current output palette" : baseTooltipText;
                                EditorGUI.BeginDisabledGroup(!isPaletteNameValid);
                                if (GUILayout.Button(new GUIContent("New from Current", currentTooltip)))
                                {
                                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Creating new palette from current: {_customPaletteName}");
                                    _ = Task.Run(async () => await CreateNewPalette(_customPaletteName, false));
                                }
                                EditorGUI.EndDisabledGroup();
                            }
                            EditorGUILayout.Space(4f); // Add padding under buttons
                        }

                        GUILayout.Space(8f);

                        // Right box: Current Palette
                        using (EditorGUILayout.VerticalScope rightBox = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            EditorGUILayout.LabelField("Current Palette", _centeredLabel);
                            EditorGUILayout.Space(4f);

                            string[] paletteOptions = new string[_existingPalettePaths.Count];
                            for (int i = 0; i < _existingPalettePaths.Count; i++)
                            {
                                string fileName = Path.GetFileNameWithoutExtension(_existingPalettePaths[i]);
                                paletteOptions[i] = fileName;
                            }

                            // Add empty option if no palettes exist
                            if (_existingPalettePaths.Count == 0)
                            {
                                paletteOptions = new string[] { "No palettes found" };
                            }

                            int newIndex = EditorGUILayout.Popup(_selectedPaletteIndex, paletteOptions);
                            if (newIndex != _selectedPaletteIndex && _existingPalettePaths.Count > 0)
                            {
                                _selectedPaletteIndex = newIndex;
                                _outputPath = _existingPalettePaths[_selectedPaletteIndex];
                                _filename = Path.GetFileNameWithoutExtension(_outputPath);

                                // Always reset output palette when selection changes
                                ResetOutputPaletteForSelection();

                                // Reset new texture flag when selecting existing palette
                                bool wasNewTexture = _isNewTexture;
                                if (ENABLE_DEBUG_LOGS) Debug.Log($"Palette selection changed to index {_selectedPaletteIndex}, resetting _isNewTexture from {wasNewTexture} to false");
                                _isNewTexture = false;

                                // If this was a new texture, log a warning that the flag was reset
                                if (wasNewTexture)
                                {
                                    if (ENABLE_DEBUG_LOGS) Debug.LogWarning("⚠️ _isNewTexture flag was reset by palette selection - will rely on file creation time check");
                                }
                            }
                            EditorGUILayout.Space(1f); // Add padding under dropdown
                        }
                        GUILayout.Space(10f); // Right padding to balance left padding
                    }

                    // Bottom row: Palette helpers
                    EditorGUILayout.Space(16f);
                    using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(10f); // Left padding to align with palette boxes above

                        // Left: Source Texture Palette (only show when not at color limit)
                        using (EditorGUILayout.VerticalScope sourceBox = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width((AvailableWidth - 60f) * 0.5f)))
                        {
                            if (!_showColorLimitWarning)
                            {
                                DrawPaletteHelper(_sourceColors, _sourceColorsCount, "Source Texture Palette", false);
                            }
                            else
                            {
                                // Show limited source palette when at color limit
                                DrawPaletteHelper(_sourceColors, _sourceColorsCount, $"Source Texture Palette (Limited to {_sourceColorsCount})", false);
                            }
                        }

                        GUILayout.Space(8f);

                        // Right: Output Texture Palette (always show, may contain all colors)
                        using (EditorGUILayout.VerticalScope outputBox = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            DrawPaletteHelper(_newColors, _newColorsCount, "Output Texture Palette", true, !_useHsvTool, _sourceColors);
                        }
                        GUILayout.Space(10f); // Right padding to balance left padding
                    }

                    // HSV Tool Section (under palette settings)
                    EditorGUILayout.Space(3f);
                    bool newHsvToolState;
                    using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.Space(8f);
                        newHsvToolState = EditorGUILayout.Toggle(_useHsvTool, GUILayout.Width(16));
                        EditorGUILayout.LabelField("HSV Tool (will revert current output palette to source colors)", EditorStyles.label, GUILayout.Width(500));
                        GUILayout.FlexibleSpace();
                    }
                    if (newHsvToolState != _useHsvTool)
                    {
                        _useHsvTool = newHsvToolState;
                        if (_useHsvTool)
                        {
                            // Reset output palette to source colors when enabling HSV tool
                            ResetOutputPalette();
                        }
                    }

                    if (_useHsvTool)
                    {
                        using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.Space(8f);
                            using (EditorGUILayout.VerticalScope hsvVerticalScope = new EditorGUILayout.VerticalScope())
                            {
                                EditorGUI.indentLevel++;

                                EditorGUILayout.LabelField("Hue Shift (-180 to 180)", EditorStyles.boldLabel);
                                float newHueShift = EditorGUILayout.Slider(_hsvHueShift, -180f, 180f);
                                if (newHueShift != _hsvHueShift)
                                {
                                    _hsvHueShift = newHueShift;
                                    ApplyHsvAdjustments();
                                }

                                EditorGUILayout.LabelField("Saturation Shift (-100 to 100)", EditorStyles.boldLabel);
                                float newSatShift = EditorGUILayout.Slider(_hsvSaturationShift, -100f, 100f);
                                if (newSatShift != _hsvSaturationShift)
                                {
                                    _hsvSaturationShift = newSatShift;
                                    ApplyHsvAdjustments();
                                }

                                EditorGUILayout.LabelField("Value Shift (-100 to 100)", EditorStyles.boldLabel);
                                float newValShift = EditorGUILayout.Slider(_hsvValueShift, -100f, 100f);
                                if (newValShift != _hsvValueShift)
                                {
                                    _hsvValueShift = newValShift;
                                    ApplyHsvAdjustments();
                                }

                                EditorGUI.indentLevel--;
                            }
                            GUILayout.FlexibleSpace();
                        }
                    }

                    // Add space under HSV tool section
                    EditorGUILayout.Space(6f);
                }
            }

            // Add padding under palette settings group only when auto-save is enabled
            if (_autoSwap)
            {
                EditorGUILayout.Space(20f);
            }
        }



        private void ResetOutputPalette()
        {
            if (_source && _sourceColors == null)
            {
                if (USE_ASYNC)
                {
                    _getPalettesTask = Task.Run(GetPalettes, _cancellationToken);
                }
                else GetPalettes();
            }
            else
            {
                // Use full source colors (not limited) for output palette
                if (_fullSourceColors != null)
                {
                    _newColors = new List<Color32>(_fullSourceColors);
                    _newColorsCount = _newColors.Count;
                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Reset output palette with all {_newColorsCount} source colors (including those beyond limit)");
                }
                else
                {
                    _newColors = new List<Color32>(_sourceColors);
                    _newColorsCount = _newColors.Count;
                }

                // If auto-save is enabled, save immediately
                if (_autoSwap)
                {
                    if (USE_ASYNC)
                    {
                        _swapColorsTask = Task.Run(SwapPalette, _cancellationToken);
                    }
                    else
                    {
                        SwapPalette();
                    }
                    // Copy texture import settings on main thread after swap is complete
                    _ = Task.Run(async () => await CopyTextureImportSettings());

                    _lastOutputTextureUpdate = 0f; // Reset timer to allow immediate update
                }
            }
        }

        private void ResetPaletteButton()
        {
            if (GUILayout.Button("Reset Output Palette", EditorStyles.miniButton))
            {
                ResetOutputPalette();
            }
        }

        private async void SwapPaletteButton()
        {
            // Ensure button styles are initialized
            if (_lightGreenButton == null || _lightRedButton == null)
            {
                Initialization();
            }

            string buttonText = _sourceColors == null ? "Get Palette" : (_showColorLimitWarning ? "Color Limit Reached" : (_autoSwap ? "Auto Saving" : "Save Texture"));
            bool buttonEnabled = (!_autoSwap || _sourceColors == null) && !_showColorLimitWarning;
            GUIStyle buttonStyle = _showColorLimitWarning ? _lightRedButton : _lightGreenButton;

            // Check if enough time has elapsed since the last update
            float timeSinceLastUpdate = Time.realtimeSinceStartup - _lastUpdateTime;
            bool timeRestrictionMet = timeSinceLastUpdate >= MIN_TIME_BETWEEN_UPDATES;
            if (!timeRestrictionMet && _sourceColors != null)
            {
                float timeRemaining = MIN_TIME_BETWEEN_UPDATES - timeSinceLastUpdate;
                buttonText = $"Wait {timeRemaining:F1}s";
            }

            EditorGUI.BeginDisabledGroup(!buttonEnabled || (!timeRestrictionMet && _sourceColors != null));
            if (GUILayout.Button(buttonText, buttonStyle, GUILayout.Height(40), GUILayout.Width(200)))
            {
                if (!_source) throw new Exception("Source Texture is not valid!");

                // Block saving when color limit is reached
                if (_showColorLimitWarning) return;

                if (_sourceColors == null)
                {
                    if (USE_ASYNC)
                    {
                        _getPalettesTask = Task.Run(GetPalettes, _cancellationToken);
                        await _getPalettesTask;
                        Repaint();
                    }
                    else GetPalettes();

                    return;
                }

                // Update the last update time before starting the swap
                _lastUpdateTime = Time.realtimeSinceStartup;

                if (USE_ASYNC)
                {
                    _swapColorsTask = Task.Run(SwapPalette, _cancellationToken);
                    await _swapColorsTask;
                }
                else
                {
                    _lazySwapper = new LazySwapper
                    {
                        ColorXYMap = _map,
                        OutputPath = _outputPath
                    };

                    _lazySwapper.SwapColors(_sourcePath, _newColors);
                }

                // Copy texture import settings on main thread after swap is complete
                await CopyTextureImportSettings();

                // Handle post-save logic for new palettes (check if this was a new texture)
                bool wasNewTexture = _isNewTexture;
                HandlePostSaveLogic(wasNewTexture);

                _lastOutputTextureUpdate = 0f; // Reset timer to allow immediate update
            }
            EditorGUI.EndDisabledGroup();
        }

        #endregion

        #region HELPER METHODS

        private void GetPalettes()
        {
            if (ENABLE_DEBUG_LOGS) Debug.Log($"GetPalettes() called with source path: {_sourcePath}");
            ResetColorLists();

            _map = LazyColorFinder.GetColorMap(_sourcePath, out _sourceColors, (byte)_ignorePixelsWithAlpha);
            if (ENABLE_DEBUG_LOGS) Debug.Log($"LazyColorFinder.GetColorMap returned {_sourceColors?.Count ?? 0} source colors");

            // Store full source colors before applying limit
            _fullSourceColors = new List<Color32>(_sourceColors);
            _totalSourceColorsCount = _sourceColors?.Count ?? 0;

            // Apply color limit to source colors
            if (_sourceColors != null && _sourceColors.Count > _paletteColorLimit)
            {
                _sourceColors = _sourceColors.Take(_paletteColorLimit).ToList();
                _showColorLimitWarning = true;
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Applied color limit, reduced to {_sourceColors.Count} colors");
            }
            else
            {
                _showColorLimitWarning = false;
            }

            // Check if output texture already exists
            if (File.Exists(_outputPath))
            {
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Output texture exists at: {_outputPath}");
                // Load palette from existing output texture
                List<Color32> existingOutputColors;
                LazyColorFinder.GetColorMap(_outputPath, out existingOutputColors, (byte)_ignorePixelsWithAlpha);
                _newColors = new List<Color32>(existingOutputColors);
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Loaded {_newColors.Count} colors from existing output texture");
            }
            else
            {
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Output texture does not exist, using source colors as starting point");
                // Use source colors as starting point
                _newColors = new List<Color32>(_sourceColors);
            }

            _sourceColorsCount = _sourceColors.Count;
            _newColorsCount = _newColors.Count;
            if (ENABLE_DEBUG_LOGS) Debug.Log($"GetPalettes() completed - Source colors: {_sourceColorsCount}, New colors: {_newColorsCount}");
        }

        private void SwapPalette()
        {
            _lazySwapper = new LazySwapper
            {
                ColorXYMap = _map,
                OutputPath = _outputPath
            };

            _lazySwapper.SwapColors(_sourcePath, _newColors);

            // Ensure file write operation is fully completed
            File.SetLastWriteTimeUtc(_outputPath, DateTime.UtcNow);

            // Give the file system a moment to settle
            Thread.Sleep(100);

            // Note: AssetDatabase.Refresh() will be called on main thread in ExecuteAutoSwap
        }

        private void ResetColorLists()
        {
            if (ENABLE_DEBUG_LOGS) Debug.Log("ResetColorLists() - Clearing color lists");
            _sourceColors = null;
            _sourceColorsCount = 0;
            _fullSourceColors = null;
            _totalSourceColorsCount = 0;

            _newColors = null;
            _newColorsCount = 0;
            if (ENABLE_DEBUG_LOGS) Debug.Log("ResetColorLists() - Color lists cleared");
        }

        private void ResetOutputPaletteForSelection()
        {
            // Check if output texture exists
            if (File.Exists(_outputPath))
            {
                // Load palette from existing output texture
                try
                {
                    List<Color32> existingOutputColors;
                    LazyColorFinder.GetColorMap(_outputPath, out existingOutputColors, (byte)_ignorePixelsWithAlpha);
                    _newColors = new List<Color32>(existingOutputColors);
                    _newColorsCount = _newColors.Count;
                }
                catch (System.Exception e)
                {
                    if (ENABLE_DEBUG_LOGS) Debug.LogWarning($"Failed to load palette from existing texture: {e.Message}");
                    // Fallback to source colors
                    if (_sourceColors != null)
                    {
                        _newColors = new List<Color32>(_sourceColors);
                        _newColorsCount = _newColors.Count;
                    }
                }
            }
            else
            {
                // Output texture doesn't exist, set output palette to source colors
                if (_sourceColors != null)
                {
                    _newColors = new List<Color32>(_sourceColors);
                    _newColorsCount = _newColors.Count;
                }
            }
        }

        private void FindExistingPalettes(string directory, string baseFileName)
        {
            _existingPalettePaths.Clear();
            _selectedPaletteIndex = 0;

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(baseFileName))
                return;

            string[] files = Directory.GetFiles(directory, $"{baseFileName}_palette_*{_extension}");
            foreach (string file in files)
            {
                _existingPalettePaths.Add(file);
            }
        }

        private bool IsPaletteNameTaken(string paletteName)
        {
            if (string.IsNullOrEmpty(paletteName))
                return false;

            // Check if the palette name already exists in the existing palettes list (case-insensitive)
            foreach (string existingPath in _existingPalettePaths)
            {
                string existingFileName = Path.GetFileNameWithoutExtension(existingPath);
                if (existingFileName.EndsWith($"_palette_{paletteName}", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateOutputPath()
        {
            if (string.IsNullOrEmpty(_sourcePath))
                return;

            string sourcePathDirectory = Path.GetDirectoryName(_sourcePath);
            string originalFilename = Path.GetFileNameWithoutExtension(_sourcePath);

            if (!string.IsNullOrEmpty(_customPaletteName))
            {
                _filename = $"{originalFilename}_palette_{_customPaletteName}";
            }
            else
            {
                _filename = $"{originalFilename}_palette_New";
            }

            _outputPath = Path.Combine(sourcePathDirectory, $"{_filename}{_extension}");
        }

        private async Task CreateNewPalette(string paletteName, bool resetToSource = true)
        {
            if (ENABLE_DEBUG_LOGS) Debug.Log($"CreateNewPalette called with: {paletteName}, resetToSource: {resetToSource}");

            if (string.IsNullOrEmpty(_sourcePath))
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogWarning("Cannot create new palette: source path is empty");
                return;
            }

            if (string.IsNullOrEmpty(paletteName))
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogWarning("Cannot create new palette: palette name is empty");
                return;
            }

            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source path: {_sourcePath}");

            // Update the custom palette name and output path
            _customPaletteName = paletteName;
            UpdateOutputPath();

            if (ENABLE_DEBUG_LOGS) Debug.Log($"Updated output path: {_outputPath}");

            // Add the new palette to the existing palettes list
            if (!_existingPalettePaths.Contains(_outputPath))
            {
                _existingPalettePaths.Add(_outputPath);
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Added to existing palettes list. Total count: {_existingPalettePaths.Count}");
            }
            else
            {
                if (ENABLE_DEBUG_LOGS) Debug.Log("Palette already exists in list");
            }

            // Update the selected palette index to point to the new palette
            _selectedPaletteIndex = _existingPalettePaths.Count - 1;
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Selected palette index: {_selectedPaletteIndex}");

            // Mark this as a new texture that needs import settings
            _isNewTexture = true;
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Marked as new texture - _isNewTexture set to true at frame {Time.frameCount}");

            // Disable HSV tool when creating new palette
            _useHsvTool = false;

            // Set output palette colors based on resetToSource parameter
            if (resetToSource)
            {
                // Reset output palette colors to source colors (blank palette)
                if (_sourceColors != null)
                {
                    _newColors = new List<Color32>(_sourceColors);
                    _newColorsCount = _newColors.Count;
                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Reset output palette colors to source. Color count: {_newColorsCount}");
                }
                else
                {
                    if (ENABLE_DEBUG_LOGS) Debug.LogWarning("Source colors are null, cannot reset output palette");
                }
            }
            else
            {
                // Copy current output palette colors (palette copy)
                if (_newColors != null)
                {
                    _newColors = new List<Color32>(_newColors);
                    _newColorsCount = _newColors.Count;
                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Copied current output palette colors. Color count: {_newColorsCount}");
                }
                else
                {
                    if (ENABLE_DEBUG_LOGS) Debug.LogWarning("Current output colors are null, cannot copy palette");
                }
            }

            // Clear the new palette name field
            _customPaletteName = string.Empty;

            // Immediately create the texture file and copy import settings
            if (_sourceColors != null && _newColors != null)
            {
                if (ENABLE_DEBUG_LOGS) Debug.Log("=== CREATE NEW PALETTE: Starting texture creation ===");
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Source colors count: {_sourceColors.Count}");
                if (ENABLE_DEBUG_LOGS) Debug.Log($"New colors count: {_newColors.Count}");
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Map count: {_map?.Count ?? 0}");
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Output path: {_outputPath}");

                // Create the texture using LazySwapper
                _lazySwapper = new LazySwapper
                {
                    ColorXYMap = _map,
                    OutputPath = _outputPath
                };

                if (ENABLE_DEBUG_LOGS) Debug.Log("Calling LazySwapper.SwapColors...");
                _lazySwapper.SwapColors(_sourcePath, _newColors);
                if (ENABLE_DEBUG_LOGS) Debug.Log("LazySwapper.SwapColors completed");

                // Ensure file write is completed
                System.IO.File.SetLastWriteTimeUtc(_outputPath, DateTime.UtcNow);

                // Wait a moment before refreshing
                await Task.Delay(150);

                // Force asset database refresh to prevent modification time errors
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // Check if file was created
                if (File.Exists(_outputPath))
                {
                    if (ENABLE_DEBUG_LOGS) Debug.Log($"✓ Texture file created successfully at: {_outputPath}");

                    // Wait another moment before importing
                    await Task.Delay(100);

                }
                else
                {
                    if (ENABLE_DEBUG_LOGS) Debug.LogError($"✗ Texture file was NOT created at: {_outputPath}");
                }

                // Force AssetDatabase refresh to register the new file
                AssetDatabase.ImportAsset(_outputPath, ImportAssetOptions.ForceUpdate);


                // Copy import settings immediately after creation
                if (ENABLE_DEBUG_LOGS) Debug.Log("Calling CopyTextureImportSettings...");
                await CopyTextureImportSettings();
                if (ENABLE_DEBUG_LOGS) Debug.Log("CopyTextureImportSettings completed");

                // Note: _isNewTexture is reset inside CopyTextureImportSettings()

                if (ENABLE_DEBUG_LOGS) Debug.Log("=== CREATE NEW PALETTE: Texture creation completed ===");
            }
            else
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogError($"Cannot create texture: _sourceColors is null: {_sourceColors == null}, _newColors is null: {_newColors == null}");
            }

            // Force UI refresh to update the palette display
            Repaint();
            if (ENABLE_DEBUG_LOGS) Debug.Log("CreateNewPalette completed successfully");
        }

        private async Task CopyTextureImportSettings()
        {
            if (ENABLE_DEBUG_LOGS) Debug.Log("=== COPY TEXTURE IMPORT SETTINGS: Starting ===");

            // Ensure file system is synchronized and add a delay
            System.IO.File.SetLastWriteTimeUtc(_outputPath, DateTime.UtcNow);
            await Task.Delay(200); // Add 200ms delay for file system synchronization

            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source path: {_sourcePath}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Output path: {_outputPath}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"_isNewTexture: {_isNewTexture}");

            if (string.IsNullOrEmpty(_sourcePath) || string.IsNullOrEmpty(_outputPath))
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogError("✗ Cannot copy import settings: Source or output path is empty");
                return;
            }

            // Check if this is a new texture by examining the file
            bool isActuallyNewTexture = false;
            if (File.Exists(_outputPath))
            {
                // Check if file was created very recently (within last 5 seconds)
                FileInfo fileInfo = new FileInfo(_outputPath);
                TimeSpan timeSinceCreation = DateTime.Now - fileInfo.CreationTime;
                isActuallyNewTexture = timeSinceCreation.TotalSeconds < 5.0;

                if (ENABLE_DEBUG_LOGS) Debug.Log($"File creation time: {fileInfo.CreationTime}");
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Time since creation: {timeSinceCreation.TotalSeconds:F2} seconds");
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Is actually new texture: {isActuallyNewTexture}");
            }

            // Use either the flag or the file check
            if (!_isNewTexture && !isActuallyNewTexture)
            {
                if (ENABLE_DEBUG_LOGS) Debug.Log("✗ Cannot copy import settings: _isNewTexture is false and file is not recently created");
                return;
            }

            // Check if output file exists
            if (!File.Exists(_outputPath))
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogError($"✗ Cannot copy import settings: Output file does not exist at {_outputPath}");
                return;
            }

            // Get the source texture importer
            TextureImporter sourceImporter = AssetImporter.GetAtPath(_sourcePath) as TextureImporter;
            if (sourceImporter == null)
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogError($"✗ Cannot copy import settings: Source importer is null for {_sourcePath}");
                return;
            }
            if (ENABLE_DEBUG_LOGS) Debug.Log($"✓ Source importer found for: {_sourcePath}");

            // Get the output texture importer (with retry for newly created files)
            TextureImporter outputImporter = null;
            int retryCount = 0;
            const int maxRetries = 10; // Increased retries

            while (outputImporter == null && retryCount < maxRetries)
            {
                outputImporter = AssetImporter.GetAtPath(_outputPath) as TextureImporter;
                if (outputImporter == null)
                {
                    retryCount++;
                    if (ENABLE_DEBUG_LOGS) Debug.Log($"Waiting for Unity to register texture file... (attempt {retryCount}/{maxRetries})");

                    // Force AssetDatabase refresh and wait longer
                    AssetDatabase.Refresh();
                    await Task.Delay(200); // Increased delay to 200ms
                }
            }

            if (outputImporter == null)
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogError($"✗ Cannot copy import settings: Output importer is null for {_outputPath} after {maxRetries} attempts");
                return;
            }
            if (ENABLE_DEBUG_LOGS) Debug.Log($"✓ Output importer found for: {_outputPath}");

            // Log current settings before copying
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source texture type: {sourceImporter.textureType}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source sprite pixels per unit: {sourceImporter.spritePixelsPerUnit}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source filter mode: {sourceImporter.filterMode}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source wrap mode: {sourceImporter.wrapMode}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source compression: {sourceImporter.textureCompression}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source mipmap enabled: {sourceImporter.mipmapEnabled}");
            if (ENABLE_DEBUG_LOGS) Debug.Log($"Source alpha is transparency: {sourceImporter.alphaIsTransparency}");

            // Copy the important settings
            outputImporter.textureType = sourceImporter.textureType;
            outputImporter.spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
            outputImporter.filterMode = sourceImporter.filterMode;
            outputImporter.wrapMode = sourceImporter.wrapMode;
            outputImporter.textureCompression = sourceImporter.textureCompression;
            outputImporter.mipmapEnabled = sourceImporter.mipmapEnabled;
            outputImporter.alphaIsTransparency = sourceImporter.alphaIsTransparency;

            if (ENABLE_DEBUG_LOGS) Debug.Log("✓ All import settings copied");

            // Apply the changes
            if (ENABLE_DEBUG_LOGS) Debug.Log("Calling SaveAndReimport...");
            outputImporter.SaveAndReimport();
            if (ENABLE_DEBUG_LOGS) Debug.Log("✓ SaveAndReimport completed");

            // Reset the flag after copying import settings
            _isNewTexture = false;
            if (ENABLE_DEBUG_LOGS) Debug.Log("Reset _isNewTexture to false");

            if (ENABLE_DEBUG_LOGS) Debug.Log("Calling AssetDatabase.Refresh...");
            AssetDatabase.Refresh();
            if (ENABLE_DEBUG_LOGS) Debug.Log("✓ AssetDatabase.Refresh completed");

            // Final verification - ensure the asset is properly registered
            AssetDatabase.Refresh();

            if (ENABLE_DEBUG_LOGS) Debug.Log("=== COPY TEXTURE IMPORT SETTINGS: Completed successfully ===");
        }

        private void QueueAutoSwap()
        {
            if (!_autoSwap || _sourceColors == null || _newColors == null || _showColorLimitWarning) return;

            // Check if enough time has elapsed since the last update
            float timeSinceLastUpdate = Time.realtimeSinceStartup - _lastUpdateTime;
            if (timeSinceLastUpdate < MIN_TIME_BETWEEN_UPDATES)
            {
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Throttling update: {timeSinceLastUpdate:F2}s since last update, minimum is {MIN_TIME_BETWEEN_UPDATES:F2}s");
                // Still queue the update but it will wait in ExecuteAutoSwap
            }

            _pendingAutoSwap = true;
        }

        private async void ExecuteAutoSwap()
        {
            if (!_pendingAutoSwap || _sourceColors == null || _newColors == null) return;

            // Check if we need to wait to respect the minimum time between updates
            float timeSinceLastUpdate = Time.realtimeSinceStartup - _lastUpdateTime;
            if (timeSinceLastUpdate < MIN_TIME_BETWEEN_UPDATES)
            {
                float timeToWait = MIN_TIME_BETWEEN_UPDATES - timeSinceLastUpdate;
                if (ENABLE_DEBUG_LOGS) Debug.Log($"Waiting {timeToWait:F2}s before updating texture");
                await Task.Delay((int)(timeToWait * 1000));
            }

            _pendingAutoSwap = false;

            try
            {
                // Update the last update time before starting the swap
                _lastUpdateTime = Time.realtimeSinceStartup;

                if (USE_ASYNC)
                {
                    _swapColorsTask = Task.Run(SwapPalette, _cancellationToken);
                    await _swapColorsTask;
                }
                else
                {
                    SwapPalette();
                }

                // Force asset database refresh on main thread after swap is complete
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

                // Add a delay before copying import settings
                await Task.Delay(150);

                // Check if this was a new texture before copying import settings
                bool wasNewTexture = _isNewTexture;

                // Copy texture import settings on main thread after swap is complete
                await CopyTextureImportSettings();

                // Handle post-save logic for new palettes
                HandlePostSaveLogic(wasNewTexture);

                _lastOutputTextureUpdate = 0f; // Reset timer to allow immediate update
            }
            catch (Exception e)
            {
                if (ENABLE_DEBUG_LOGS) Debug.LogError($"Auto-swap failed: {e.Message}");
            }
        }

        private void HandlePostSaveLogic(bool wasNewTexture)
        {
            // If this was a new texture and the file was created successfully
            if (wasNewTexture && File.Exists(_outputPath))
            {
                // Clear the custom palette name field
                _customPaletteName = string.Empty;

                // Refresh the palette list to include the newly created texture
                string sourcePathDirectory = Path.GetDirectoryName(_sourcePath);
                string originalFilename = Path.GetFileNameWithoutExtension(_sourcePath);
                FindExistingPalettes(sourcePathDirectory, originalFilename);

                // Find and select the newly created palette
                for (int i = 0; i < _existingPalettePaths.Count; i++)
                {
                    if (_existingPalettePaths[i] == _outputPath)
                    {
                        _selectedPaletteIndex = i;
                        break;
                    }
                }

                // Reset the new texture flag
                _isNewTexture = false;

                // Force UI refresh
                Repaint();
            }
        }

        private bool Color32Equals(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        }

        private void DrawPaletteHelper(List<Color32> colors, int colorCount, string label, bool isEditable = false, bool allowEditing = true, List<Color32> sourceColors = null, params GUILayoutOption[] options)
        {
            // Ensure button styles are initialized
            if (_yellowButton == null)
            {
                Initialization();
            }

            using (EditorGUILayout.VerticalScope verticalScope = new EditorGUILayout.VerticalScope(options))
            {
                EditorGUILayout.LabelField(label, _centeredHelpBoxLabel);

                for (int i = 0; i < colorCount; i++)
                {
                    using (EditorGUILayout.HorizontalScope horizontalScope = new EditorGUILayout.HorizontalScope())
                    {
                        if (!isEditable || !allowEditing)
                        {
                            try
                            {
                                // Show alpha bar if the color has any alpha (not fully opaque)
                                bool showAlpha = colors[i].a < 255f;
                                EditorGUILayout.ColorField(GUIContent.none, colors[i], false, showAlpha, false);
                            }
                            catch (Exception e)
                            {
                                if (ENABLE_DEBUG_LOGS) Debug.LogError($"Error drawing color field: {e.Message}");
                            }
                        }
                        else
                        {
                            Color32 oldColor = colors[i];
                            // Show alpha bar if the color has any alpha (not fully opaque)
                            bool showAlpha = colors[i].a < 255f;
                            colors[i] = EditorGUILayout.ColorField(GUIContent.none, colors[i], true, showAlpha, false);

                            // Check if color changed and auto-swap is enabled
                            if (!Color32Equals(oldColor, colors[i]))
                            {
                                if (_autoSwap)
                                {
                                    QueueAutoSwap();
                                }
                            }
                        }

                        // Add reset button for output texture palette
                        if (isEditable && sourceColors != null && i < sourceColors.Count)
                        {
                            bool isSameColor = Color32Equals(colors[i], sourceColors[i]);
                            EditorGUI.BeginDisabledGroup(isSameColor);

                            Color originalColor = GUI.backgroundColor;
                            if (!isSameColor)
                                GUI.backgroundColor = new Color(0, 1, 1, .2f);
                            if (GUILayout.Button(new GUIContent("", "Revert to source color"), GUILayout.Width(20), GUILayout.Height(16)))
                            {
                                colors[i] = sourceColors[i];
                                if (_autoSwap)
                                {
                                    QueueAutoSwap();
                                }
                            }
                            GUI.backgroundColor = originalColor; // Always restore
                            EditorGUI.EndDisabledGroup();
                        }
                        // Only add empty space for source palette to align with output palette
                        else if (!isEditable)
                        {
                        }
                    }
                }

                // Add reset button only for output texture palette
                if (isEditable && label.Contains("Output"))
                {
                    if (GUILayout.Button("Reset Output Palette"))
                    {
                        ResetOutputPalette();
                    }
                }
            }
        }

        private void ApplyHsvAdjustments()
        {
            if (!_useHsvTool || _sourceColors == null || _newColors == null)
                return;

            // Reset output palette to source colors first
            _newColors = new List<Color32>(_sourceColors);
            _newColorsCount = _newColors.Count;

            // Apply HSV adjustments to each color
            for (int i = 0; i < _newColors.Count; i++)
            {
                Color32 originalColor = _newColors[i];

                // Convert to HSV
                Color.RGBToHSV(originalColor, out float h, out float s, out float v);

                // Apply shifts
                h = (h + _hsvHueShift / 360f) % 1f;
                if (h < 0f) h += 1f;

                s = Mathf.Clamp01(s + _hsvSaturationShift / 100f);
                v = Mathf.Clamp01(v + _hsvValueShift / 100f);

                // Convert back to RGB
                Color adjustedColor = Color.HSVToRGB(h, s, v);
                adjustedColor.a = originalColor.a; // Preserve alpha

                _newColors[i] = adjustedColor;
            }

            // Trigger auto-swap if enabled
            if (_autoSwap)
            {
                QueueAutoSwap();
            }
        }

        #endregion

        #region HELPER METHODS

        private Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        #endregion

        #region INITIALIZATION METHOD

        private void Initialization()
        {
            // Create centered label style that allows word wrap
            _centeredLabel = new GUIStyle(EditorStyles.label);
            _centeredLabel.alignment = TextAnchor.MiddleCenter;
            _centeredLabel.wordWrap = true;
            _centeredLabel.clipping = TextClipping.Overflow;
            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;

            if (_sourceContent == null) _sourceContent = new GUIContent("Source Texture2D:");

            if (_titleLabel == null)
            {
                _titleLabel = new GUIStyle()
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 32,
                    font = Resources.Load<Font>("Fonts/kenney-fonts/MiniSquare_Editor")
                };
                _titleLabel.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            }

            if (_headerLabel == null)
            {
                _headerLabel = new GUIStyle()
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 24,
                    font = Resources.Load<Font>("Fonts/kenney-fonts/MiniSquare_Editor")
                };
                _headerLabel.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            }

            if (_centeredHelpBoxLabel == null)
            {
                _centeredHelpBoxLabel = new GUIStyle(EditorStyles.helpBox)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                };
                _centeredHelpBoxLabel.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            }

            if (_centeredLabel == null)
            {
                _centeredLabel = new GUIStyle()
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                };
                _centeredLabel.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            }

            if (_lightBlueButton == null)
            {
                _lightBlueButton = new GUIStyle(GUI.skin.button)
                {
                    normal = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.4f, 0.6f, 1.0f)) },
                    hover = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.5f, 0.7f, 1.0f)) },
                    active = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.3f, 0.5f, 0.9f)) }
                };
            }

            if (_lightGreenButton == null)
            {
                _lightGreenButton = new GUIStyle(GUI.skin.button)
                {
                    normal = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.1f, 0.5f, 0.1f)) },
                    hover = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.2f, 0.6f, 0.2f)) },
                    active = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.05f, 0.4f, 0.05f)) }
                };
            }

            if (_lightRedButton == null)
            {
                _lightRedButton = new GUIStyle(GUI.skin.button)
                {
                    normal = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.8f, 0.4f, 0.4f)) },
                    hover = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.9f, 0.5f, 0.5f)) },
                    active = { textColor = Color.white, background = MakeTexture(2, 2, new Color(0.7f, 0.3f, 0.3f)) }
                };
            }

            if (_yellowButton == null)
            {
                _yellowButton = new GUIStyle(GUI.skin.button)
                {
                    normal = { textColor = Color.black, background = MakeTexture(2, 2, new Color(1.0f, 0.8f, 0.2f)) },
                    hover = { textColor = Color.black, background = MakeTexture(2, 2, new Color(1.0f, 0.9f, 0.3f)) },
                    active = { textColor = Color.black, background = MakeTexture(2, 2, new Color(0.9f, 0.7f, 0.1f)) }
                };
            }

            if (_desaturatedRedLabel == null)
            {
                _desaturatedRedLabel = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black }, // Default text color
                    hover = { textColor = new Color(0.6f, 0.3f, 0.3f) }, // Very desaturated red on hover
                    clipping = TextClipping.Overflow // Allow text to overflow
                };
            }

            if (_boldCenteredLabel == null)
            {
                _boldCenteredLabel = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12,
                };
                _boldCenteredLabel.normal.textColor = EditorGUIUtility.isProSkin ? Color.white : Color.black;
            }
        }

        #endregion

        #region EDITOR PREFS

        private void LoadEditorPrefs()
        {
            // Load source texture path
            string savedSourcePath = EditorPrefs.GetString(EDITOR_PREFS_KEY_SOURCE_PATH, "");
            if (!string.IsNullOrEmpty(savedSourcePath) && File.Exists(savedSourcePath))
            {
                _source = AssetDatabase.LoadAssetAtPath<Texture2D>(savedSourcePath);
                if (_source != null)
                {
                    _sourcePath = savedSourcePath;

                    string sourcePathDirectory = Path.GetDirectoryName(_sourcePath);
                    string originalFilename = Path.GetFileNameWithoutExtension(_sourcePath);
                    _extension = $"{Path.GetExtension(_sourcePath)}";

                    // Find existing palette textures
                    FindExistingPalettes(sourcePathDirectory, originalFilename);

                    // Set default palette name based on existing palettes
                    if (_existingPalettePaths.Count == 0)
                    {
                        _customPaletteName = "New";
                    }
                    else
                    {
                        _customPaletteName = string.Empty;
                    }

                    // Set default output path
                    _filename = $"{originalFilename}_palette_New";
                    _outputPath = Path.Combine(sourcePathDirectory, $"{_filename}{_extension}");

                    // Load palette data
                    if (USE_ASYNC)
                    {
                        _getPalettesTask = Task.Run(GetPalettes, _cancellationToken);
                    }
                    else
                    {
                        GetPalettes();
                    }
                }
            }

            // Load advanced settings
            _ignorePixelsWithAlpha = EditorPrefs.GetInt(EDITOR_PREFS_KEY_IGNORE_PIXELS_WITH_ALPHA, 255);
            _autoSwap = EditorPrefs.GetBool(EDITOR_PREFS_KEY_AUTO_SAVE_TEXTURE, true);
            _showAdvancedSettings = EditorPrefs.GetBool(EDITOR_PREFS_KEY_SHOW_ADVANCED_SETTINGS, false);
            _showInstructions = EditorPrefs.GetBool(EDITOR_PREFS_KEY_SHOW_INSTRUCTIONS, false);
            _paletteColorLimit = EditorPrefs.GetInt(EDITOR_PREFS_KEY_PALETTE_COLOR_LIMIT, 16);
            _previewHeight = EditorPrefs.GetInt(EDITOR_PREFS_KEY_PREVIEW_HEIGHT, 100);


        }

        private void SaveEditorPrefs()
        {
            // Save source texture path
            EditorPrefs.SetString(EDITOR_PREFS_KEY_SOURCE_PATH, _sourcePath ?? "");

            // Save advanced settings
            EditorPrefs.SetInt(EDITOR_PREFS_KEY_IGNORE_PIXELS_WITH_ALPHA, _ignorePixelsWithAlpha);
            EditorPrefs.SetBool(EDITOR_PREFS_KEY_AUTO_SAVE_TEXTURE, _autoSwap);
            EditorPrefs.SetBool(EDITOR_PREFS_KEY_SHOW_ADVANCED_SETTINGS, _showAdvancedSettings);
            EditorPrefs.SetBool(EDITOR_PREFS_KEY_SHOW_INSTRUCTIONS, _showInstructions);
            EditorPrefs.SetInt(EDITOR_PREFS_KEY_PALETTE_COLOR_LIMIT, _paletteColorLimit);
            EditorPrefs.SetInt(EDITOR_PREFS_KEY_PREVIEW_HEIGHT, _previewHeight);


        }

        #endregion

        private void DrawAdvancedSettings()
        {
            bool newShowAdvancedSettings = EditorGUILayout.Foldout(_showAdvancedSettings, "Settings", true);
            if (newShowAdvancedSettings != _showAdvancedSettings)
            {
                _showAdvancedSettings = newShowAdvancedSettings;
                SaveEditorPrefs();
            }
            if (!_showAdvancedSettings) return;
            using (EditorGUILayout.HorizontalScope asd = new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f); // Left padding
                using (EditorGUILayout.VerticalScope verticalScope = new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(300)))
                {
                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.Space(1f);
                    _autoSwap = EditorGUILayout.ToggleLeft(new GUIContent("Auto Save Texture", "Automatically save texture when colors are changed!"), _autoSwap);
                    EditorGUILayout.Space(8f);

                    // Store old alpha value to detect changes
                    int oldIgnorePixelsWithAlpha = _ignorePixelsWithAlpha;
                    _ignorePixelsWithAlpha = EditorGUILayout.IntSlider(new GUIContent("Alpha Ignore Threshold:", $"Skip pixels with alpha < {_ignorePixelsWithAlpha}. Examples: 0=ignore only fully transparent, 128=ignore 50%+ transparent, 255=ignore all except fully opaque"), _ignorePixelsWithAlpha, 0, 255);

                    EditorGUILayout.Space(8f);

                    // Store old color limit value to detect changes
                    int oldPaletteColorLimit = _paletteColorLimit;
                    _paletteColorLimit = EditorGUILayout.IntSlider(new GUIContent("Palette Color Limit:", $"Maximum number of unique colors allowed in a palette. Current limit: {_paletteColorLimit}"), _paletteColorLimit, 10, 50);
                    if (EditorGUI.EndChangeCheck())
                    {
                        SaveEditorPrefs();

                        // If alpha threshold changed and we have a source texture, refresh the palettes
                        if (oldIgnorePixelsWithAlpha != _ignorePixelsWithAlpha && _source != null)
                        {
                            if (ENABLE_DEBUG_LOGS) Debug.Log($"Alpha threshold changed from {oldIgnorePixelsWithAlpha} to {_ignorePixelsWithAlpha}, refreshing palettes");

                            // Reset color lists and get new palettes
                            ResetColorLists();

                            if (USE_ASYNC)
                            {
                                _getPalettesTask = Task.Run(GetPalettes, _cancellationToken);
                            }
                            else
                            {
                                GetPalettes();
                            }

                            // If no output texture exists, reset output palette to source colors
                            if (!File.Exists(_outputPath))
                            {
                                ResetOutputPalette();
                            }
                        }

                        // If color limit changed and we have a source texture, reimport and run normal operations
                        if (oldPaletteColorLimit != _paletteColorLimit && _source != null)
                        {
                            if (ENABLE_DEBUG_LOGS) Debug.Log($"Color limit changed from {oldPaletteColorLimit} to {_paletteColorLimit}, reimporting source texture");

                            // Reimport the source texture to ensure it's up to date
                            AssetDatabase.ImportAsset(_sourcePath, ImportAssetOptions.ForceUpdate);

                            // Reset color lists and get new palettes
                            ResetColorLists();

                            if (USE_ASYNC)
                            {
                                _getPalettesTask = Task.Run(GetPalettes, _cancellationToken);
                            }
                            else
                            {
                                GetPalettes();
                            }

                            // If no output texture exists, reset output palette to source colors
                            if (!File.Exists(_outputPath))
                            {
                                ResetOutputPalette();
                            }
                        }
                    }
                }
            }

            // Add padding below advanced settings
            EditorGUILayout.Space(10f);
        }
    }
}
#endif