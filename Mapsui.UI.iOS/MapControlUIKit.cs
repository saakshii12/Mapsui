using Mapsui.Fetcher;
using Mapsui.Rendering.Skia;
using CoreFoundation;
using Foundation;
using UIKit;
using System;
using System.ComponentModel;
using CoreGraphics;
using SkiaSharp.Views.iOS;
using Math = System.Math;

namespace Mapsui.UI.iOS
{
    [Register("MapControlUIKit"), DesignTimeVisible(true)]
    public class MapControlUIKit :  SKCanvasView
    {
        public delegate void ViewportInitializedEventHandler(object sender);
        public event ViewportInitializedEventHandler ViewportInitializedEvent;

        private CGPoint _previousMid;
        private CGPoint _currentMid;
        private float _oldDist = 1f;
        private MapRenderer _renderer;
        private Map _map;
        
        private bool _viewportInitialized;
        public bool ViewportInitialized
        {
            get { return _viewportInitialized; }
            set
            {
                _viewportInitialized = value;
                if (_viewportInitialized && ViewportInitializedEvent != null) ViewportInitializedEvent(this);
            }
        }

        private float Width { get { return (float)Frame.Width; } }
        private float Height { get { return (float)Frame.Height; } }

        public MapControlUIKit(CGRect frame)
            : base(frame)
        {
            Initialize();
        }

        public void Initialize()
        {
            Map = new Map();
            BackgroundColor = UIColor.White;
            _renderer = new MapRenderer();

            InitializeViewport();
            
            ClipsToBounds = true;

            var pinchGesture = new UIPinchGestureRecognizer(PinchGesture) { Enabled = true };
            AddGestureRecognizer(pinchGesture);

            PaintSurface += OnPaintSurface;
        }

        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs skPaintSurfaceEventArgs)
        {
            if (!ViewportInitialized) InitializeViewport();
            if (!ViewportInitialized) return;

            if (Width != _map.Viewport.Width) _map.Viewport.Width = Width;
            if (Height != _map.Viewport.Height) _map.Viewport.Height = Height;

            var scaleFactor = 2; // todo: figure out how to get this value programatically
            skPaintSurfaceEventArgs.Surface.Canvas.Scale((float)scaleFactor, (float)scaleFactor);

            _renderer.Render(skPaintSurfaceEventArgs.Surface.Canvas, _map.Viewport, _map.Layers, _map.BackColor);
        }

        private void InitializeViewport()
        {
            if (Math.Abs(Width - 0f) < Utilities.Constants.Epsilon) return;
            if (_map == null) return;
            if (_map.Envelope == null) return;
            if (Math.Abs(_map.Envelope.Width - 0d) < Utilities.Constants.Epsilon) return;
            if (Math.Abs(_map.Envelope.Height - 0d) < Utilities.Constants.Epsilon) return;
            if (_map.Envelope.GetCentroid() == null) return;

            if (double.IsNaN(_map.Viewport.Resolution) || double.IsInfinity(_map.Viewport.Resolution))
                _map.Viewport.Resolution = _map.Envelope.Width / Width;
            if ((double.IsNaN(_map.Viewport.Center.X)) || double.IsNaN(_map.Viewport.Center.Y) ||
                double.IsInfinity(_map.Viewport.Center.X) || double.IsInfinity(_map.Viewport.Center.Y))
                _map.Viewport.Center = _map.Envelope.GetCentroid();

            _map.Viewport.Width = Width;
            _map.Viewport.Height = Height;
            _map.Viewport.RenderResolutionMultiplier = 1;

            _map.ViewChanged(true);
            _viewportInitialized = true;
        }

        private void PinchGesture(UIPinchGestureRecognizer recognizer)
        {
			if (_map.Lock) return;

            if ((int)recognizer.NumberOfTouches < 2)
                return;

            if (recognizer.State == UIGestureRecognizerState.Began)
            {
                _oldDist = 1;
                _currentMid = recognizer.LocationInView(this);
            }

            float scale = 1 - (_oldDist - (float)recognizer.Scale);

            if (scale > 0.5 && scale < 1.5)
            {
                if (_oldDist != (float)recognizer.Scale)
                {
                    _oldDist = (float)recognizer.Scale;
                    _currentMid = recognizer.LocationInView(this);
                    _previousMid = new CGPoint(_currentMid.X, _currentMid.Y);

                    _map.Viewport.Center = _map.Viewport.ScreenToWorld(
                        _currentMid.X,
                        _currentMid.Y);
                    _map.Viewport.Resolution = _map.Viewport.Resolution / scale;
                    _map.Viewport.Center = _map.Viewport.ScreenToWorld(
                        (_map.Viewport.Width - _currentMid.X),
                        (_map.Viewport.Height - _currentMid.Y));
                }

                _map.Viewport.Transform(
                    _currentMid.X,
                    _currentMid.Y,
                    _previousMid.X,
                    _previousMid.Y);

                RefreshGraphics();
            }

            var majorChange = recognizer.State == UIGestureRecognizerState.Ended;
            _map.ViewChanged(majorChange);
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
			if (_map.Lock) return;

            if ((uint)touches.Count == 1)
            {
                var touch = touches.AnyObject as UITouch;
                if (touch != null)
                {
                    var currentPos = touch.LocationInView(this);
                    var previousPos = touch.PreviousLocationInView(this);

                    var cRect = new CGRect(new CGPoint((int)currentPos.X, (int)currentPos.Y), new CGSize(5, 5));
                    var pRect = new CGRect(new CGPoint((int)previousPos.X, (int)previousPos.Y), new CGSize(5, 5));

                    if (!cRect.IntersectsWith(pRect))
                    {
                        _map.Viewport.Transform(currentPos.X, currentPos.Y, previousPos.X, previousPos.Y);

                        RefreshGraphics();
                    }
                }
            }
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            //base.TouchesEnded (touches, evt);
            RefreshGraphics();
            _map.ViewChanged(true);
        }

        /// <summary>
        /// LoadContent will be called once per game and is the place to load
        /// all of your content.
        /// </summary>
        protected void LoadContent()
        {
        }

        /// <summary>
        /// UnloadContent will be called once per game and is the place to unload
        /// all content.
        /// </summary>
        protected void UnloadContent()
        {
        }

        public Map Map
        {
            get
            {
                return _map;
            }
            set
            {
                if (_map != null)
                {
                    var temp = _map;
                    _map = null;
                    temp.DataChanged -= MapDataChanged;
                    temp.PropertyChanged -= MapPropertyChanged;
                    temp.RefreshGraphics -= MapRefreshGraphics;
                    temp.Dispose();
                }

                _map = value;
                
                if (_map != null)
                {
                    _map.DataChanged += MapDataChanged;
                    _map.PropertyChanged += MapPropertyChanged;
                    _map.RefreshGraphics += MapRefreshGraphics;
                    _map.ViewChanged(true);
                }

                RefreshGraphics();
            }
        }

        private void MapRefreshGraphics(object sender, EventArgs eventArgs)
        {
            RefreshGraphics();
        }

        private void MapPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
			if (e.PropertyName == "Enabled")
			{
				RefreshGraphics();
			}
			else if (e.PropertyName == "Opacity")
			{
				RefreshGraphics();
			}
			else if (e.PropertyName == "Envelope")
			{
				InitializeViewport();
				_map.ViewChanged(true);
			}
			else if (e.PropertyName == "Rotation") // not supported yet
			{
				RefreshGraphics();
				_map.ViewChanged(true);
			}
        }

        public void MapDataChanged(object sender, DataChangedEventArgs e)
        {
            string errorMessage;

            DispatchQueue.MainQueue.DispatchAsync(delegate
            {
                if (e == null)
                {
                    errorMessage = "MapDataChanged Unexpected error: DataChangedEventArgs can not be null";
                    Console.WriteLine(errorMessage);
                }
                else if (e.Cancelled)
                {
                    errorMessage = "MapDataChanged: Cancelled";
                    System.Diagnostics.Debug.WriteLine(errorMessage);
                }
                else if (e.Error is System.Net.WebException)
                {
                    errorMessage = "MapDataChanged WebException: " + e.Error.Message;
                    Console.WriteLine(errorMessage);
                }
                else if (e.Error != null)
                {
                    errorMessage = "MapDataChanged errorMessage: " + e.Error.GetType() + ": " + e.Error.Message;
                    Console.WriteLine(errorMessage);
                }

                RefreshGraphics();
            });
        }

        private void RefreshGraphics()
        {
            SetNeedsDisplay();
        }
    }
}