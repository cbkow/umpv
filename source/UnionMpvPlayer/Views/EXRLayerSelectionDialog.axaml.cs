using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using UnionMpvPlayer.Helpers;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Interactivity;
using UnionMpvPlayer.Models;
using UnionMpvPlayer.Views;

namespace UnionMpvPlayer.Views
{
    public partial class EXRLayerSelectionDialog : Window
    {
        private bool _isCancelled;
        private Dictionary<string, EXRLayer> _availableLayers;
        private readonly EXRSequenceHandler _sequenceHandler;
        private readonly string _firstFramePath;

        public string SelectedLayer { get; private set; }

        public EXRLayerSelectionDialog(string exrFilePath)
        {
            InitializeComponent();
            FrameRateInput = this.FindControl<TextBox>("FrameRateInput");
            _firstFramePath = exrFilePath;
            _sequenceHandler = new EXRSequenceHandler();
            _isCancelled = false;

            Loaded += EXRLayerSelectionDialog_Loaded;
        }

        private async void EXRLayerSelectionDialog_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (this.FindControl<ProgressBar>("LoadingProgress") is ProgressBar progress)
                {
                    progress.IsVisible = true;
                }

                _availableLayers = await _sequenceHandler.AnalyzeEXRFile(_firstFramePath);
                UpdateLayerList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error analyzing EXR: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Error", $"Failed to analyze EXR file: {ex.Message}", this);
            }
            finally
            {
                if (this.FindControl<ProgressBar>("LoadingProgress") is ProgressBar progress)
                {
                    progress.IsVisible = false;
                }
            }
        }

        public async Task InitializeLayerList(string exrFilePath)
        {
            try
            {
                if (this.FindControl<ProgressBar>("LoadingProgress") is ProgressBar progress)
                {
                    progress.IsVisible = true;
                }

                if (!File.Exists(exrFilePath))
                {
                    throw new FileNotFoundException($"EXR file not found: {exrFilePath}");
                }

                _availableLayers = await _sequenceHandler.AnalyzeEXRFile(exrFilePath);
                if (_availableLayers == null || _availableLayers.Count == 0)
                {
                    throw new Exception("No layers found in EXR file");
                }

                UpdateLayerList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error analyzing EXR: {ex.Message}");
                var toast = new ToastView();
                toast.ShowToast("Error", $"Failed to analyze EXR file: {ex.Message}", this);
            }
            finally
            {
                if (this.FindControl<ProgressBar>("LoadingProgress") is ProgressBar progress)
                {
                    progress.IsVisible = false;
                }
            }
        }

        private void UpdateLayerList()
        {
            if (_availableLayers == null) return;

            var items = new List<LayerListItem>();

            // Add displayable multi-channel layers
            foreach (var layer in _availableLayers)
            {
                if (_sequenceHandler.IsDisplayableLayer(layer.Value))
                {
                    items.Add(new LayerListItem
                    {
                        Name = layer.Key,
                        Channels = layer.Value.Channels,
                        HasRGB = layer.Value.HasRGB,
                        HasRGBA = layer.Value.HasRGBA
                    });
                }
            }

            // Sort the items alphabetically
            items = items.OrderBy(l => l.Name).ToList();

            // Update the ListBox
            if (this.FindControl<ListBox>("LayerList") is ListBox layerList)
            {
                layerList.ItemsSource = items;

                if (items.Any())
                {
                    layerList.SelectedItem = items[0];
                    SelectedLayer = items[0].Name;
                }
            }
        }




        private void CancelButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close(false);
        }

        private async void OkButton_Click(object? sender, RoutedEventArgs e)
        {
            var layerList = this.FindControl<ListBox>("LayerList");

            if (layerList?.SelectedItem is LayerListItem selectedLayer && !string.IsNullOrEmpty(FrameRateInput.Text))
            {
                SelectedLayer = selectedLayer.Name;

                if (SelectedLayer == "Base Color")
                {
                    try
                    {
                        var mainWindow = (MainWindow)Owner;
                        Close(true); // Close the dialog before starting sequence processing
                        await mainWindow.HandleImageSequenceFromEXR(_firstFramePath, FrameRateInput.Text);
                    }
                    catch (Exception ex)
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Error", $"Failed to process base color: {ex.Message}", this);
                    }
                }
                else if (_availableLayers.TryGetValue(SelectedLayer, out var layer))
                {
                    if (!_sequenceHandler.IsDisplayableLayer(layer))
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Warning", "Selected layer does not contain displayable channels.", this);
                        return;
                    }

                    try
                    {
                        var mainWindow = (MainWindow)Owner;
                        Close(true); // Close the dialog before starting sequence processing
                        await mainWindow.HandleEXRSequence(_firstFramePath, SelectedLayer, FrameRateInput.Text);
                    }
                    catch (Exception ex)
                    {
                        var toast = new ToastView();
                        toast.ShowToast("Error", $"Failed to process sequence: {ex.Message}", this);
                    }
                }
            }
            else
            {
                var toast = new ToastView();
                toast.ShowToast("Warning", "Please select a layer and enter a valid frame rate.", this);
            }
        }

    }
}