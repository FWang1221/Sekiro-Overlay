ioFuncsExample.txt contains all the code on the hks side of things to establish a connection to the console app. the console app must be run in admin mode.

after setting up some basic commands and logic (the overlay is slow, i'd assume a max of 10 commands a second should be your limit) go and acquire dotnet 7.0 SDK and open visual studio. by the way, all mod users need dotnet 7.0 (not SDK) if you have this sort of feature with the overlay and stuff.

look at Examples.cs, after building the project.

go to the bottom.

you see stuff like this:

        [CommandHandler("SekiWorldSpeedSet")]
        public class SekiWorldSpeedSetCommandHandler : ICommandHandler
        {
            public void HandleCommand(pipedSekiroResponse data)
            {
                Example.gameSpeeder.PatchGameSpeed(float.Parse(data.getArgs()[0]) / 100);
            }
        }

this is the factory design pattern, and use it to make different commands. SekiWorldSpeedSet, SekiSpeedSet, and Style Meter are off limits. to do more with the functions n stuff like with the overlay itself, you'll need to create some functions and call it somewhere in function _window_DrawGraphics, though I'm working on making that too a factory design pattern.

if you have any questions, join discord.gg/servername and @f_wang#0. include the words "hks" and "blueberries" or else i won't respond.