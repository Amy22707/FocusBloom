using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FocusBloom.Desktop.ViewModels;
using XamlAnimatedGif;

namespace FocusBloom.Desktop.Views;

public partial class FocusBackgroundView : UserControl
{
    public static readonly DependencyProperty BackgroundImageProperty = DependencyProperty.Register(nameof(BackgroundImage), typeof(ImageSource), typeof(FocusBackgroundView));
    public static readonly DependencyProperty AnimatedUriProperty = DependencyProperty.Register(nameof(AnimatedUri), typeof(Uri), typeof(FocusBackgroundView), new PropertyMetadata(null, (sender, _) => ((FocusBackgroundView)sender).UpdateAnimation()));
    public static readonly DependencyProperty VideoUriProperty = DependencyProperty.Register(nameof(VideoUri), typeof(Uri), typeof(FocusBackgroundView), new PropertyMetadata(null, (sender, _) => ((FocusBackgroundView)sender).UpdateAnimation()));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(FocusBackgroundView), new PropertyMetadata(0d, (sender, _) => ((FocusBackgroundView)sender).UpdateClip()));
    public ImageSource? BackgroundImage { get => (ImageSource?)GetValue(BackgroundImageProperty); set => SetValue(BackgroundImageProperty, value); }
    public Uri? AnimatedUri { get => (Uri?)GetValue(AnimatedUriProperty); set => SetValue(AnimatedUriProperty, value); }
    public Uri? VideoUri { get => (Uri?)GetValue(VideoUriProperty); set => SetValue(VideoUriProperty, value); }
    private Uri? _playingVideo;
    public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public FocusBackgroundView()
    {
        InitializeComponent();
        AnimationBehavior.SetRepeatBehavior(GifImage, RepeatBehavior.Forever);
        AnimationBehavior.AddErrorHandler(GifImage, (_, args) =>
        {
            AnimationBehavior.SetSourceUri(GifImage, null);
            args.Handled = true;
            (DataContext as FocusMediaViewModel)?.ReportGifPlaybackError();
        });
        Loaded += (_, _) => UpdateAnimation();
        Unloaded += (_, _) => { AnimationBehavior.SetSourceUri(GifImage, null); CloseVideo(); };
        IsVisibleChanged += (_, _) => UpdateAnimation();
        SizeChanged += (_, _) => UpdateClip();
    }

    private void UpdateAnimation()
    {
        if (!IsLoaded || !IsVisible)
        {
            AnimationBehavior.SetSourceUri(GifImage, null);
            CloseVideo();
            return;
        }
        Dispatcher.InvokeAsync(() =>
        {
            var uri = IsLoaded && IsVisible ? AnimatedUri : null;
            if (!Equals(AnimationBehavior.GetSourceUri(GifImage), uri)) AnimationBehavior.SetSourceUri(GifImage, uri);
            var video = IsLoaded && IsVisible ? VideoUri : null;
            if (!Equals(_playingVideo, video))
            {
                CloseVideo();
                if (video is not null)
                {
                    _playingVideo = video; VideoPlayer.Source = video;
                    VideoPlayer.Play();
                }
            }
        });
    }
    private void CloseVideo()
    {
        if (_playingVideo is null) return;
        _playingVideo = null; VideoPlayer.Close(); VideoPlayer.Source = null;
    }
    private void VideoEnded(object sender, RoutedEventArgs args)
    {
        if (_playingVideo is null || !IsVisible) return;
        VideoPlayer.Position = TimeSpan.Zero; VideoPlayer.Play();
    }
    private void VideoFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (_playingVideo is null) return;
        CloseVideo();
        (DataContext as FocusMediaViewModel)?.ReportVideoPlaybackError();
    }
    private void UpdateClip() => Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius, CornerRadius);

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        return new Size();
    }
}
