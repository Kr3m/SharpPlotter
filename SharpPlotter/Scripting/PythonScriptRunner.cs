using System;
using System.Collections.Generic;
using System.Linq;
using IronPython.Hosting;
using IronPython.Runtime;
using IronPython.Runtime.Exceptions;
using Microsoft.Scripting;
using Microsoft.Scripting.Hosting;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;

namespace SharpPlotter.Scripting
{
    public class PythonScriptRunner : IScriptRunner
    {
        private readonly ScriptEngine _scriptEngine;

        public string NewFileHeaderContent => @"
# Python script for SharpPlotter
#
# Points on the graph can be defined as a tuple value of x and y coordinates, such as `(1,2)`.  
#
# Colors can be defined by calling the `Color(r, g, b)` function with integer values between 0 and 255.  A set of 
#    predefined colors exist as properties on the `Color` object, such as `Color.Green`, `Color.CornflowerBlue`, etc..
#
# The graph can be drawn on by calling the `graph.Points()` function to draw isolated points, or the `graph.Segments()`
#    function which draws line segments from one point to the next.  Points can be specified individually by hand
#    (e.g. `graph.Points((1,1), (1,2), (1,3))`) or they can be passed in via an array.  Points and segments will be
#    white unless the first parameter of the function is a color value.
#
  
".TrimStart();

        public PythonScriptRunner()
        {
            _scriptEngine = Python.CreateEngine();
            _scriptEngine.Runtime.LoadAssembly(typeof(Color).Assembly);
        }
        
        public GraphedItems RunScript(string scriptContent)
        {
            var scope = _scriptEngine.CreateScope();
            var items = new GraphedItems();
            var drawMethods = new DrawMethods(items);
            scope.SetVariable("graph", drawMethods);

            scriptContent = $"from Microsoft.Xna.Framework import Color{Environment.NewLine}{scriptContent}";
            var script = _scriptEngine.CreateScriptSourceFromString(scriptContent);

            try
            {
                script.Execute(scope);
            }
            catch (Exception exception)
            {
                if (exception is SyntaxErrorException ||
                    exception is AttributeErrorException ||
                    exception is MissingMemberException)
                {
                    throw new ScriptException("Syntax Error", false, exception);
                }

                throw;
            }

            return items;
        }
        
        // ReSharper disable once MemberCanBePrivate.Global
        // If this is not public, then the scripting engine won't see the methods and a MissingMemberException
        // will occur when calling them.
        public class DrawMethods
        {
            private readonly GraphedItems _graphedItems;

            public DrawMethods(GraphedItems graphedItems)
            {
                _graphedItems = graphedItems;
            }

            public void Points(params object[] objects)
            {
                objects ??= Array.Empty<object>();
                var (color, points) = ParseObjects(objects);
                _graphedItems.AddPoints(color, points);
            }

            public void Segments(params object[] objects)
            {
                objects ??= Array.Empty<object>();
                var (color, points) = ParseObjects(objects);
                _graphedItems.AddSegments(color, points);
            }

            private static (Color color, Point2d[] points) ParseObjects(params object[] objects)
            {
                // Due to python's dynamic nature, the incoming objects can be one of several type of objects
                var color = Color.White;
                var points = new List<Point2d>();
                foreach (var obj in objects)
                {
                    switch (obj)
                    {
                        case null:
                            throw new PointConversionException("Cannot convert `null` to a point");
                        
                        case Color passedInColor:
                            color = passedInColor;
                            break;

                        case PythonTuple pythonTuple when pythonTuple.Count == 2 &&
                                                          (pythonTuple[0] is double || pythonTuple[0] is int ||
                                                           pythonTuple[0] is float) &&
                                                          (pythonTuple[1] is double || pythonTuple[1] is int ||
                                                           pythonTuple[1] is float):
                        {
                            var x = (float) Convert.ToDouble(pythonTuple[0]);
                            var y = (float) Convert.ToDouble(pythonTuple[1]);
                            
                            points.Add(new Point2d(x, y));
                            break;
                        }
                        
                        // Iron python passes in arrays as a generic List
                        case List list:
                            var (_, innerPoints) = ParseObjects(list.ToArray());
                            points.AddRange(innerPoints);
                            break;
                            
                        // No known way to parse out the point
                        default:
                            var json = JsonConvert.SerializeObject(obj);
                            throw new PointConversionException($"No known way to convert '{json}' to a point");
                    }
                }

                return (color, points.ToArray());
            }
        }
    }
}