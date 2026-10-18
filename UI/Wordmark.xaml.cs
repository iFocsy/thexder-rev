using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Thexder.UI
{
    internal partial class Wordmark
    {
        private bool paused;

        public Wordmark()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateShine();
        }

        /// <summary>Holds the light sweep.</summary>
        public bool Paused
        {
            get => paused;
            set
            {
                paused = value;
                UpdateShine();
            }
        }

        // The light crosses the wordmark once every few seconds.
        private void UpdateShine()
        {
            if (paused || !IsLoaded || !SystemParameters.ClientAreaAnimation)
            {
                ShineShift.BeginAnimation(TranslateTransform.XProperty, null);
                ShineShift.X = -1;
                return;
            }
            var shine = new DoubleAnimationUsingKeyFrames { BeginTime = TimeSpan.FromMilliseconds(900), Duration = TimeSpan.FromSeconds(6), RepeatBehavior = RepeatBehavior.Forever };
            shine.KeyFrames.Add(new DiscreteDoubleKeyFrame(-1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            shine.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1300)), new SineEase { EasingMode = EasingMode.EaseInOut }));
            ShineShift.BeginAnimation(TranslateTransform.XProperty, shine);
        }
    }
}
