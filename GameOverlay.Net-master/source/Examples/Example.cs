using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Windows;
using GameOverlay.Drawing;
using GameOverlay.Windows;
using SharpDX.DirectWrite;
using System.Diagnostics;
using System.Threading;
using BeatDetectorCSharp;
using System.Security.Cryptography;
using SekiroFpsUnlockAndMore;
using static SekiroFpsUnlockAndMore.MainWindow;
using System.ComponentModel;
using System.Windows.Threading;
using SekiroFpsUnlockAndMore;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;

namespace Examples
{
public class Example : IDisposable
    {
        private readonly GraphicsWindow _window;

        private readonly Dictionary<string, SolidBrush> _brushes;
        private readonly Dictionary<string, GameOverlay.Drawing.Font> _fonts;
        private readonly Dictionary<string, Image> _images;

        private GameOverlay.Drawing.Geometry _gridGeometry;
        private Rectangle _gridBounds;

        private Random _random;
        private long _lastRandomSet;
        private List<Action<Graphics, float, float>> _randomFigures;

        public static MainWindow gameSpeeder = null;

        public Example()
        {
            _brushes = new Dictionary<string, SolidBrush>();
            _fonts = new Dictionary<string, GameOverlay.Drawing.Font>();
            _images = new Dictionary<string, Image>();

            var gfx = new Graphics()
            {
                MeasureFPS = true,
                PerPrimitiveAntiAliasing = true,
                TextAntiAliasing = true
            };

            _window = new GraphicsWindow(0, 0, 1920, 1080, gfx)
            {
                FPS = 60,
                IsTopmost = true,
                IsVisible = true
            };

            _window.DestroyGraphics += _window_DestroyGraphics;
            _window.DrawGraphics += _window_DrawGraphics;
            _window.SetupGraphics += _window_SetupGraphics;
        }

        

        public class globalVars {
			public static bool isSekiroRunning = false;
			public static bool threadLineage = false;
			
            // runs piped commands, uses the factory design pattern
			public static void runSekiroDataFuncs() {
                try
                {
                    sekiroDataHandler.sekiroData.ForEach(data =>
                    {
                        if (!data.hasRunYet())
                        {
                            CommandHandlerFactory.HandleCommand(data.getCommandType(), data);
                            data.setRunDone();
                            Console.WriteLine(data.getCommandType());
                        }
                    });

                    sekiroDataHandler.sekiroData.Clear();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message + "\n\n" + ex.StackTrace);
                }

            }
		}

        public class sekiroDataHandler {

            public static List<pipedSekiroResponse> sekiroData = new List<pipedSekiroResponse>();
            public static void cleanSekiroData()
            {
                sekiroData.RemoveAll(x => x.hasRunYet());
            }
            public struct styleMeterEntry
            {
                public long unixDateTime;
                public string achievement;
            }
            public static List<styleMeterEntry> styleMeter = new List<styleMeterEntry>();

            public static void cleanStyleMeter()
            {
                sekiroDataHandler.styleMeter.RemoveAll(
                    data => data.unixDateTime < (long)(DateTimeOffset.Now.ToUnixTimeSeconds() - 10)
                );
            }
        }

        // pipedSekiroResponse object, it is what the console app reads.
		public class pipedSekiroResponse {
			private string headerCommand; // the command, like "SekiWorldSpeedSet", which sets world speed multiplier
			private string[] bodyArgs; // the arguments
			private long commandDate; // when the command was issued, defunct due to how the program currently handles things + lua failings
			private bool hasRun = false; // if has run then don't

			public string getCommandType() { return headerCommand; }
			public string[] getArgs() { return bodyArgs; }
			public long getCommandDate() { return commandDate; }
			public bool hasRunYet() { return this.hasRun; }
			public void setRunDone() { this.hasRun = true; }
			public pipedSekiroResponse(string response)
			{
                string[] parts = response.Split(':');

				headerCommand = parts[0];
				bodyArgs = parts[1].Split(',');
				commandDate = (long)DateTimeOffset.Now.ToUnixTimeSeconds();

            }
			
		}

        // listener thread which creates/reloads a MainWindow object (stolen from Sekiro FPS Unlock) + listens into the game.
        // memory editing code should go here to be refreshed as this keeps track of sekiro n stuff
        static async void CSListenerSekiro()
        {
            Console.WriteLine("Named Pipe Server started...");
            var server = new NamedPipeServerStream("myPipeOutSekiroMastery", PipeDirection.InOut, 1);
            Console.WriteLine("Waiting for connection...");

            await server.WaitForConnectionAsync();


            try
            {
                if (Example.gameSpeeder == null)
                {
                    Example.gameSpeeder = new MainWindow();
                }
                else {
                    Example.gameSpeeder.reloadSelf();
                }

                
            }
            catch (Exception exc)
            {
                Console.WriteLine(exc.Message + "\n\n" + exc.StackTrace);
            }

            while (true)
            {

                try
                {
					
                    StreamReader reader = new StreamReader(server);

                    Console.WriteLine("Getting Request");
                    string request = await reader.ReadLineAsync();
                    Console.WriteLine("Got Request");
                    Console.WriteLine(request);

                    pipedSekiroResponse sekiReponse = new pipedSekiroResponse(request);
                    sekiroDataHandler.sekiroData.Add(sekiReponse);


                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{ex.Message}");
                    break;
                }
            }

			server.Disconnect();
			server.Dispose();

            Thread listener = new Thread(CSListenerSekiro);
            listener.Start(); //"recursively" generates new pipes n shiet

        }


        // defunct code, ignore
        static List<string> GetFilesInPlaylistFolder(string folderPath)
        {
            List<string> fileList = new List<string>();

            try
            {
                if (Directory.Exists(folderPath))
                {
                    string[] files = Directory.GetFiles(folderPath);

                    foreach (string file in files)
                    {
                        fileList.Add(Path.GetFileName(file));
                    }
                }
                else
                {
                    Console.WriteLine("The 'playlist' folder does not exist.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

            return fileList;
        }
        public void Beater() // uses https://github.com/Terracorrupt/BeatDetectorForGames/tree/master/BeatDetector/BeatDetectorC%23Version
        {
            BeatDetector detector = BeatDetector.Instance();

            detector.loadSystem();

            string playlistFolderPath = "playlist"; // Replace with the actual path to your "playlist" folder

            List<string> fileList = GetFilesInPlaylistFolder(playlistFolderPath);
            Random random = new Random();

            detector.LoadSong(1024, "playlist\\" + fileList[random.Next(0, fileList.Count)]);

            detector.setStarted(true);

            TimeStamp localLastBeatOccurred = new TimeStamp();

            while (true)
            {
                detector.update();
				if (!detector.isPlaying()) {
                    detector.LoadSong(1024, "playlist\\" + fileList[random.Next(0, fileList.Count)]);
                }

                if (localLastBeatOccurred != detector.getLastBeat())
                {
                    //Beat Occured

                    //DO SOMETHING

                    //Update localLastBeat
                    localLastBeatOccurred = detector.getLastBeat();


                    //Console.WriteLine("Beat Detected");
                }


            }
        }

        // overlay setup n all that
        private void _window_SetupGraphics(object sender, SetupGraphicsEventArgs e)
		{
			var gfx = e.Graphics;

			if (e.RecreateResources)
			{
				foreach (var pair in _brushes) pair.Value.Dispose();
				foreach (var pair in _images) pair.Value.Dispose();
			}

			_brushes["black"] = gfx.CreateSolidBrush(0, 0, 0);
			_brushes["white"] = gfx.CreateSolidBrush(255, 255, 255);
			_brushes["red"] = gfx.CreateSolidBrush(255, 0, 0);
			_brushes["green"] = gfx.CreateSolidBrush(0, 255, 0);
			_brushes["blue"] = gfx.CreateSolidBrush(0, 0, 255);
			_brushes["background"] = gfx.CreateSolidBrush(0x33, 0x36, 0x3F);
			_brushes["grid"] = gfx.CreateSolidBrush(255, 255, 255, 0.2f);
			_brushes["random"] = gfx.CreateSolidBrush(0, 0, 0);

			if (e.RecreateResources) return;

			_fonts["arial"] = gfx.CreateFont("Arial", 12);
			_fonts["consolas"] = gfx.CreateFont("Consolas", 14);


			_gridBounds = new Rectangle(20, 60, gfx.Width - 20, gfx.Height - 20);
			_gridGeometry = gfx.CreateGeometry();

			for (float x = _gridBounds.Left; x <= _gridBounds.Right; x += 20)
			{
				var line = new Line(x, _gridBounds.Top, x, _gridBounds.Bottom);
				_gridGeometry.BeginFigure(line);
				_gridGeometry.EndFigure(false);
			}

			for (float y = _gridBounds.Top; y <= _gridBounds.Bottom; y += 20)
			{
				var line = new Line(_gridBounds.Left, y, _gridBounds.Right, y);
				_gridGeometry.BeginFigure(line);
				_gridGeometry.EndFigure(false);
			}

			_gridGeometry.Close();

		}

		private void _window_DestroyGraphics(object sender, DestroyGraphicsEventArgs e)
		{
			foreach (var pair in _brushes) pair.Value.Dispose();
			foreach (var pair in _fonts) pair.Value.Dispose();
			foreach (var pair in _images) pair.Value.Dispose();
		}
		public void getPlayerOverlay(DrawGraphicsEventArgs eventThing)
        {
            var gfx = eventThing.Graphics;
			gfx.DrawText(_fonts["consolas"], _brushes["white"], 1200, 50, "Style Meter");
            sekiroDataHandler.cleanSekiroData();
            globalVars.runSekiroDataFuncs();
            sekiroDataHandler.cleanStyleMeter();


			long styleMeterLength = 0;
            sekiroDataHandler.styleMeter.ForEach(
				data => {
					gfx.DrawText(_fonts["consolas"], _brushes["white"], 1200, 100 + 30 * styleMeterLength, data.achievement);
					styleMeterLength++;
				}	
			);


        }
		private void _window_DrawGraphics(object sender, DrawGraphicsEventArgs e)
		{
			var gfx = e.Graphics;
			gfx.ClearScene();
			if (!globalVars.threadLineage) {
                Thread listener = new Thread(CSListenerSekiro);
                listener.Start();
                globalVars.threadLineage = true;

				//Thread musicinator = new Thread(Beater);
				//musicinator.Start();

                //temporarily disabled the rhythm shit for now until a fix can be found
            }



            getPlayerOverlay(e);

			if (_lastRandomSet == 0L || e.FrameTime - _lastRandomSet > 2500)
			{
				_lastRandomSet = e.FrameTime;
			}

		}

		public void Run()
		{
			_window.Create();
			_window.Join();
        }

		~Example()
		{
			Dispose(false);
		}

        [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
        sealed class CommandHandlerAttribute : Attribute
        {
            public string CommandName { get; }

            public CommandHandlerAttribute(string commandName)
            {
                CommandName = commandName;
            }
        }

        public interface ICommandHandler
        {
            void HandleCommand(pipedSekiroResponse data);
        }

        public static class CommandHandlerFactory
        {
            private static readonly Dictionary<string, ICommandHandler> CommandHandlers = new Dictionary<string, ICommandHandler>();

            static CommandHandlerFactory()
            {
                // Use reflection to discover command handler classes
                var handlerTypes = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(type => typeof(ICommandHandler).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract);

                foreach (var handlerType in handlerTypes)
                {
                    var attribute = (CommandHandlerAttribute)Attribute.GetCustomAttribute(handlerType, typeof(CommandHandlerAttribute));
                    if (attribute != null)
                    {
                        var handlerInstance = (ICommandHandler)Activator.CreateInstance(handlerType);
                        CommandHandlers[attribute.CommandName] = handlerInstance;
                    }
                }
            }

            public static void HandleCommand(string commandName, pipedSekiroResponse data)
            {
                if (CommandHandlers.TryGetValue(commandName, out var handler))
                {
                    handler.HandleCommand(data);
                }
            }
        }

        [CommandHandler("Style Meter")]
        public class StyleMeterCommandHandler : ICommandHandler
        {
            public void HandleCommand(pipedSekiroResponse data)
            {
                sekiroDataHandler.styleMeterEntry styleMeterPoint = new sekiroDataHandler.styleMeterEntry();
                styleMeterPoint.unixDateTime = data.getCommandDate();
                styleMeterPoint.achievement = data.getArgs()[0];

                sekiroDataHandler.styleMeter.Add(styleMeterPoint);
            }
        }

        [CommandHandler("SekiSpeedSet")]
        public class SekiSpeedSetCommandHandler : ICommandHandler
        {
            public void HandleCommand(pipedSekiroResponse data)
            {
                Example.gameSpeeder.PatchPlayerSpeed(float.Parse(data.getArgs()[0]) / 100);
            }
        }

        [CommandHandler("SekiWorldSpeedSet")]
        public class SekiWorldSpeedSetCommandHandler : ICommandHandler
        {
            public void HandleCommand(pipedSekiroResponse data)
            {
                Example.gameSpeeder.PatchGameSpeed(float.Parse(data.getArgs()[0]) / 100);
            }
        }


        #region IDisposable Support
        private bool disposedValue;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				_window.Dispose();

				disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		#endregion
	}
}
