using Avalonia;
using ReactiveUI;
using System.Reactive.Linq;

namespace UnionMpvPlayer.ViewModels
{
    public class MainWindowViewModel : ReactiveObject
    {
        private string _topTitle = "umpv";

        public string TopTitle
        {
            get => _topTitle;
            set => this.RaiseAndSetIfChanged(ref _topTitle, value);
        }
    }
}
