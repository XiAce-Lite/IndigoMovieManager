using System.Windows;
using System.Windows.Controls;

namespace IndigoMovieManager.BottomTabs.ThumbnailProgress
{
    public partial class ThumbnailProgressTabView : UserControl
    {
        public ThumbnailProgressTabView()
        {
            InitializeComponent();
        }

        public event RoutedEventHandler GpuDecodeEnabledClicked;
        public event RoutedPropertyChangedEventHandler<double> ParallelismSliderValueChanged;
        public event RoutedPropertyChangedEventHandler<double> SlowLaneMinGbSliderValueChanged;
        public event RoutedEventHandler PresetFastRequested;
        public event RoutedEventHandler PresetNormalRequested;
        public event RoutedEventHandler PresetLowLoadRequested;
        public event RoutedEventHandler PresetSsdRequested;
        public event RoutedEventHandler PresetMaxRequested;

        // MainWindow 側は host 経由でだけ内部要素へ触る。
        public CheckBox ResizeThumbCheckBox => ThumbnailProgressResizeThumbCheckBox;
        public CheckBox GpuDecodeEnabledCheckBox => ThumbnailProgressGpuDecodeEnabled;
        public Slider ParallelismSlider => sliderThumbnailProgressParallelism;
        public Slider SlowLaneMinGbSlider => sliderThumbnailProgressSlowLaneMinGb;
        public RadioButton PresetLowSpeedRadioButton => ThumbnailProgressPresetLowSpeedRadioButton;
        public RadioButton PresetSsdRadioButton => ThumbnailProgressPresetSsdRadioButton;
        public RadioButton PresetNormalRadioButton => ThumbnailProgressPresetNormalRadioButton;
        public RadioButton PresetFastRadioButton => ThumbnailProgressPresetFastRadioButton;
        public RadioButton PresetMaxRadioButton => ThumbnailProgressPresetMaxRadioButton;

        private void ThumbnailProgressGpuDecodeEnabled_Click(object sender, RoutedEventArgs e)
        {
            GpuDecodeEnabledClicked?.Invoke(sender, e);
        }

        private void ThumbnailProgressParallelismSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            ParallelismSliderValueChanged?.Invoke(sender, e);
        }

        private void ThumbnailProgressSlowLaneMinGbSlider_ValueChanged(
            object sender,
            RoutedPropertyChangedEventArgs<double> e
        )
        {
            SlowLaneMinGbSliderValueChanged?.Invoke(sender, e);
        }

        private void ThumbnailProgressPresetFastRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PresetFastRequested?.Invoke(sender, e);
        }

        private void ThumbnailProgressPresetNormalRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PresetNormalRequested?.Invoke(sender, e);
        }

        private void ThumbnailProgressPresetSsdRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PresetSsdRequested?.Invoke(sender, e);
        }

        private void ThumbnailProgressPresetLowLoadRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PresetLowLoadRequested?.Invoke(sender, e);
        }

        private void ThumbnailProgressPresetMaxRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            PresetMaxRequested?.Invoke(sender, e);
        }
    }
}
