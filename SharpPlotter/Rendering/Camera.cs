﻿using System;
using System.Collections.Generic;
using SkiaSharp;

namespace SharpPlotter.Rendering
{
    public class Camera
    {
        /// <summary>
        /// How many pixels from the edge grid lines are allowed to extend to.  Mostly gives adequate room for labels
        /// </summary>
        private const int GridLineMargin = 20;
        
        /// <summary>
        /// How many grid lines should appear in each axis (so the display isn't cluttered when zooming out)
        /// </summary>
        private const int MaxGridLineCount = 10;
        
        /// <summary>
        /// How many pixels away from the 0 value axis a grid line should be clear from non-0 axis lines.  This cuts
        /// down on noise when a non-0 line might get drawn too close to the zero axis, and we always want the zero
        /// axis to be shown.
        /// </summary>
        private const int ZeroAxisClearance = 20;

        /// <summary>
        /// How many pixels a whole graph unit takes up by default on any given axis.  Used primarily for resetting
        /// the aspect ratio on the graph.
        /// </summary>
        private const int StandardPixelsPerGraphUnit = 90;

        private readonly OnScreenLogger _onScreenLogger;
        private readonly SKSurface _surface;
        private readonly int _width, _height, _usableWidth, _usableHeight;
        private int _basePixelsPerXUnit, _basePixelsPerYUnit;
        private Point2d _origin;
        private float _zoomFactor;

        /// <summary>
        /// The X/Y coordinates on the graph the camera is centered on
        /// </summary>
        public Point2d Origin
        {
            get => _origin;
            set
            {
                _origin = value;
                CameraHasMoved = true;
                RecalculateGraphBounds();
            }
        }

        /// <summary>
        /// How zoomed in or out the camera should be. 1 designates that it is not zoomed in or out at all, values
        /// greater than 1 are zoomed in while numbers less than 1 are zoomed out.
        /// </summary>
        public float ZoomFactor
        {
            get => _zoomFactor;
            set
            {
                // Don't allow negative or zero zoom values, as that that doesn't make sense
                // and messes up the calculations.
                if (value > 0)
                {
                    _zoomFactor = value;
                    CameraHasMoved = true;
                    RecalculateGraphBounds();
                }
            }
        }
        
        /// <summary>
        /// the smallest X and Y graph values on the visible portion of the graph
        /// </summary>
        public Point2d MinimumGraphBounds { get; private set; }
        
        /// <summary>
        /// The largest X and Y graph values on the visible portion of the graph
        /// </summary>
        public Point2d MaximumGraphBounds { get; private set; }
        
        /// <summary>
        /// If true than that means the camera is either in a new position or the zoom factor has changed.  This helps
        /// know if the view should be re-rendered or not.
        /// </summary>
        public bool CameraHasMoved { get; private set; }

        public Camera(int width, int height, OnScreenLogger onScreenLogger)
        {
            _width = width;
            _usableWidth = width - GridLineMargin * 2;
            _height = height;
            _onScreenLogger = onScreenLogger;
            _usableHeight = height - GridLineMargin * 2;

            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            _surface = SKSurface.Create(info);

            _basePixelsPerXUnit = StandardPixelsPerGraphUnit;
            _basePixelsPerYUnit = StandardPixelsPerGraphUnit;
            
            Origin = new Point2d(0, 0);
            ZoomFactor = 1f;
        }

        /// <summary>
        /// Moves the camera along the graph by pixel amounts instead of graph amounts
        /// </summary>
        public void MoveByPixelAmount(int x, int y)
        {
            var horizontalUnits = x / (_basePixelsPerXUnit * ZoomFactor);
            var verticalUnits = y / (_basePixelsPerYUnit * ZoomFactor);
            
            Origin = new Point2d(Origin.X + horizontalUnits, Origin.Y + verticalUnits);
        }

        /// <summary>
        /// Allows adjusting how many graph coordinates are viewable on a horizontal or vertical basis, instead of
        /// forcing the graph to always be locked in a specific aspect ratio.  A positive change value effectively
        /// increases the field of view and allows more values to be visible
        /// </summary>
        public void ChangeFieldOfView(int horizontalChange, int verticalChange)
        {
            _basePixelsPerXUnit -= horizontalChange;
            _basePixelsPerYUnit -= verticalChange;

            if (_basePixelsPerXUnit < 1) _basePixelsPerXUnit = 1;
            if (_basePixelsPerYUnit < 1) _basePixelsPerYUnit = 1;

            CameraHasMoved = true;
        }

        /// <summary>
        /// Resets the field of view values to match the default aspect ratio and values
        /// </summary>
        public void ResetFieldOfView()
        {
            _basePixelsPerXUnit = StandardPixelsPerGraphUnit;
            _basePixelsPerYUnit = StandardPixelsPerGraphUnit;

            CameraHasMoved = true;
        }

        /// <summary>
        /// Sets the camera to a predefined position and field of view that guarantees all 4 edges of the screen
        /// represent the defined boundary points on the graph
        /// </summary>
        public void SetGraphBounds((int min, int max) x, (int min, int max) y)
        {
            if (x.min >= x.max || y.min >= y.max)
            {
                var message = $"Invalid graph boundaries specified: X={x.min}/{x.max}, y={y.min}/{y.max}";
                _onScreenLogger.LogMessage(message);

                return;
            }

            // Without a buffer the points around the edge are right up against the edge of the canvas, which makes
            // it inconvenient to look at.  So we want to make sure there is some graph space available around the edge
            // points.
            const int boundsBufferInPixels = 40;
            
            _basePixelsPerXUnit = (_usableWidth - boundsBufferInPixels * 2) / (x.max - x.min);
            _basePixelsPerYUnit = (_usableHeight - boundsBufferInPixels * 2) / (y.max - y.min);

            var originX = x.max - (x.max - x.min) / 2f;
            var originY = y.max - (y.max - y.min) / 2f;
            
            Origin = new Point2d(originX, originY);
            ZoomFactor = 1f;
            CameraHasMoved = true;
        }

        /// <summary>
        /// Returns the X/Y point on the grid for a specific pixel coordinates.  Will return null if the pixel
        /// coordinates refer to a part of the graph that's off screen
        /// </summary>
        public Point2d? GetGraphPointForPixelCoordinates(int x, int y)
        {
            if (x < GridLineMargin || 
                y < GridLineMargin || 
                x > _usableWidth + GridLineMargin || 
                y > _usableHeight + GridLineMargin)
            {
                // Off screen
                return null;
            }

            var horizontalPixelsFromCenter = x - _width / 2;
            var verticalPixelsFromCenter = y - _height / 2;
            
            var horizontalUnitsPerPixel = _basePixelsPerXUnit * ZoomFactor;
            var verticalUnitsPerPixel = _basePixelsPerYUnit * ZoomFactor;

            var gridX = Origin.X + horizontalPixelsFromCenter / horizontalUnitsPerPixel;
            var gridY = Origin.Y - verticalPixelsFromCenter / verticalUnitsPerPixel;
            
            return new Point2d(gridX, gridY);
        }

        public SKImage Render(IReadOnlyList<RenderedPoint> points, IReadOnlyList<RenderedSegment> segments)
        {
            points ??= Array.Empty<RenderedPoint>();
            segments ??= Array.Empty<RenderedSegment>();
            
            _surface.Canvas.Clear(SKColors.Black);
            RenderGridLines();
            RenderSegments(segments);
            RenderPoints(points);

            CameraHasMoved = false;
            
            return _surface.Snapshot();
        }

        private void RecalculateGraphBounds()
        {
            var zoomedPixelsPerXUnit = _basePixelsPerXUnit * ZoomFactor;
            var zoomedPixelsPerYUnit = _basePixelsPerYUnit * ZoomFactor;
            var horizontalGraphValueCount = (int)(_usableWidth / zoomedPixelsPerXUnit);
            var verticalGraphValueCount = (int) (_usableHeight / zoomedPixelsPerYUnit);
            var minimumHorizontalGraphValue = (int) Math.Floor(Origin.X - (float) horizontalGraphValueCount / 2);
            var maximumHorizontalGraphValue = (int) Math.Ceiling(Origin.X + (float) horizontalGraphValueCount / 2);
            var minimumVerticalGraphValue = (int) Math.Floor(Origin.Y - (float) verticalGraphValueCount / 2);
            var maximumVerticalGraphValue = (int) Math.Ceiling(Origin.Y + (float) verticalGraphValueCount / 2);
            
            MinimumGraphBounds = new Point2d(minimumHorizontalGraphValue, minimumVerticalGraphValue);
            MaximumGraphBounds = new Point2d(maximumHorizontalGraphValue, maximumVerticalGraphValue);
        }

        private void RenderGridLines()
        {
            var labelPaint = new SKPaint{Color = SKColors.White, TextAlign = SKTextAlign.Center};
            var importantLinePaint = new SKPaint{Color = SKColors.White, StrokeWidth = 2};
            var standardLinePaint = new SKPaint{
                Color = SKColors.Gray, 
                StrokeWidth = 1, 
                PathEffect = SKPathEffect.CreateDash(new[] {5f, 5f}, 5f),
            };

            DrawXAxisGraphLines(standardLinePaint, labelPaint);
            DrawXAxisLineAt(0, importantLinePaint, labelPaint, true);
            DrawYAxisGraphLines(standardLinePaint, labelPaint);
            DrawYAxisLineAt(0, importantLinePaint, labelPaint, true);
        }

        private void RenderPoints(IEnumerable<RenderedPoint> points)
        {
            foreach (var point in points)
            {
                var x = GetPixelXForGraphValue((int) point.Point.X);
                var y = GetPixelYForGraphValue((int) point.Point.Y);
                var color = new SKColor(point.Color.R, point.Color.G, point.Color.B);
                
                _surface.Canvas.DrawCircle(x, y, 5, new SKPaint{Color = color});
            }
        }

        private void RenderSegments(IEnumerable<RenderedSegment> segments)
        {
            foreach (var segment in segments)
            {
                var startX = GetPixelXForGraphValue((int) segment.Start.X);
                var startY = GetPixelYForGraphValue((int) segment.Start.Y);
                var endX = GetPixelXForGraphValue((int) segment.End.X);
                var endY = GetPixelYForGraphValue((int) segment.End.Y);
                
                var start = new SKPoint(startX, startY);
                var end = new SKPoint(endX, endY);
                var color = new SKColor(segment.Color.R, segment.Color.G, segment.Color.B);
                
                _surface.Canvas.DrawLine(start, end, new SKPaint{Color = color});
            }
        }

        private void DrawXAxisGraphLines(SKPaint linePaint, SKPaint labelPaint)
        {
            var zoomedPixelsPerXUnit = _basePixelsPerXUnit * ZoomFactor;
            var totalGraphValueCount = (int)(_usableWidth / zoomedPixelsPerXUnit);
            var valueIncrement = totalGraphValueCount <= MaxGridLineCount
                ? 1
                : (int)Math.Ceiling((float)totalGraphValueCount / MaxGridLineCount);

            var minimumDisplayedValue = (int) Origin.X - totalGraphValueCount / 2;
            for (var x = 0; x <= totalGraphValueCount; x += valueIncrement)
            {
                var value = minimumDisplayedValue + x;
                DrawXAxisLineAt(value, linePaint, labelPaint);
            }
        }
        
        private void DrawYAxisGraphLines(SKPaint linePaint, SKPaint labelPaint)
        {
            var zoomedPixelsPerYUnit = _basePixelsPerYUnit * ZoomFactor;
            var totalGraphValueCount = (int)(_usableHeight / zoomedPixelsPerYUnit);
            var valueIncrement = totalGraphValueCount <= MaxGridLineCount
                ? 1
                : (int)Math.Ceiling((float)totalGraphValueCount / MaxGridLineCount);

            var minimumDisplayedValue = (int) Origin.Y - totalGraphValueCount / 2;
            for (var x = 0; x <= totalGraphValueCount; x += valueIncrement)
            {
                var value = minimumDisplayedValue + x;
                DrawYAxisLineAt(value, linePaint, labelPaint);
            }
        }

        private void DrawXAxisLineAt(int value, SKPaint linePaint, SKPaint skPaint, bool drawIfCloseToZeroAxis = false)
        {
            var pixelX = GetPixelXForGraphValue(value);
            if (pixelX < 0 || pixelX > _width)
            {
                return;
            }
            
            if (!drawIfCloseToZeroAxis && Math.Abs(pixelX - GetPixelXForGraphValue(0)) < ZeroAxisClearance)
            {
                return;
            }

            var start = new SKPoint(pixelX, GridLineMargin);
            var end = new SKPoint(pixelX, _height - GridLineMargin);
            _surface.Canvas.DrawLine(start, end, linePaint);

            var labelPoint = new SKPoint(pixelX, end.Y + 15);
            _surface.Canvas.DrawText(value.ToString(), labelPoint, skPaint);
        }
        
        private void DrawYAxisLineAt(int value, SKPaint linePaint, SKPaint skPaint, bool drawIfCloseToZeroAxis = false)
        {
            var pixelY = GetPixelYForGraphValue(value);
            if (pixelY < 0 || pixelY > _height)
            {
                return;
            }
            
            if (!drawIfCloseToZeroAxis && Math.Abs(pixelY - GetPixelYForGraphValue(0)) < ZeroAxisClearance)
            {
                return;
            }

            var start = new SKPoint(GridLineMargin, pixelY);
            var end = new SKPoint(_width - GridLineMargin, pixelY);
            _surface.Canvas.DrawLine(start, end, linePaint);

            var labelPoint = new SKPoint(GridLineMargin, pixelY);
            _surface.Canvas.DrawText(value.ToString(), labelPoint, skPaint);
        }

        private int GetPixelXForGraphValue(int value)
        {
            var zoomedPixelsPerXUnit = _basePixelsPerXUnit * ZoomFactor;
            var distanceFromOrigin = value - Origin.X;

            var pixelsFromCenter = (int)(zoomedPixelsPerXUnit * distanceFromOrigin);
            return _width / 2 + pixelsFromCenter;
        }

        private int GetPixelYForGraphValue(int value)
        {
            var zoomedPixelsPerYUnit = _basePixelsPerYUnit * ZoomFactor;
            var distanceFromOrigin = value - Origin.Y;

            var pixelsFromCenter = (int)(zoomedPixelsPerYUnit * distanceFromOrigin);
            return _height / 2 - pixelsFromCenter;
        }
    }
}