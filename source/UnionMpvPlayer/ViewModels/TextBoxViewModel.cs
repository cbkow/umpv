using Avalonia.Media;
using ReactiveUI;
using System;
using System.Reactive.Linq;
using System.Reactive;

namespace UnionMpvPlayer.ViewModels
{
    public class TextBoxViewModel : ReactiveObject
    {
        private double _x;
        public double X
        {
            get => _x;
            set => this.RaiseAndSetIfChanged(ref _x, value);
        }

        private double _y;
        public double Y
        {
            get => _y;
            set => this.RaiseAndSetIfChanged(ref _y, value);
        }

        private double _width = 100;
        public double Width
        {
            get => _width;
            set => this.RaiseAndSetIfChanged(ref _width, value);
        }

        private double _height = 40;
        public double Height
        {
            get => _height;
            set => this.RaiseAndSetIfChanged(ref _height, value);
        }

        private string _text = "Type here...";
        public string Text
        {
            get => _text;
            set => this.RaiseAndSetIfChanged(ref _text, value);
        }

        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

        public TextBoxViewModel()
        {
            RemoveCommand = ReactiveCommand.Create(() => { /* Implement removal logic */ });
        }
    }
}
