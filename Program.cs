using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// A tool for merging different map sections together. Combines elevations with objects and squares from different maps into one output map.
namespace Mappet
{
    class Square
    {
        public int elevation; // 0-2
        public int squareId;  // e.g 65537
        public string data;   // e.g grid000 grid000
    }

    class MapObject
    {
        public int id;      // obj_id
        public int tileNum; // obj_tile_num
        public Dictionary<string, string> properties = new Dictionary<string, string>(); // everything else
    }

    class Map
    {
        public Dictionary<string, string> headerData = new Dictionary<string, string>();
        public List<Square> squares = new List<Square>();
        public List<MapObject> objects = new List<MapObject>();
        public string scriptSection = "";
    }

    enum ParseSection
    {
        Pre,
        Header,  // MAP_DATA
        Squares, // MAP_SQUARES
        Scripts, // SCRIPTS
        Objects  // OBJECTS
    }

    class Config
    {

    }

    class ElevationOp
    {
        public int dstElevation;
        public int srcElevation;
        public int map;
    }

    class Program
    {
        static void SaveMap(Map map, string file)
        {
            var lines = new List<string>();
            lines.Add("");
            lines.Add(">>>>>>>>>>: MAP_DATA <<<<<<<<<<");
            lines.Add("");
            foreach (var header in map.headerData)
            {
                lines.Add($"{header.Key}: {header.Value}");
            }
            lines.Add("");
            lines.Add("");
            lines.Add(">>>>>>>>>>: MAP_SQUARES <<<<<<<<<<");
            lines.Add("");
            lines.Add("");
            foreach (var elevation in map.squares.Select(x => x.elevation).Distinct())
            {
                lines.Add($"square_elev: {elevation}");
                lines.Add("");
                foreach (var sq in map.squares.Where(x => x.elevation == elevation))
                    lines.Add($"sq: {sq.squareId} {sq.data}");
                lines.Add("");
                lines.Add("");
                lines.Add("");
            }
            lines.Add("");
            lines.Add("");
            lines.Add("");
            lines.Add(">>>>>>>>>>: SCRIPTS <<<<<<<<<<");
            lines.Add("");
            lines.Add("");
            lines.Add(map.scriptSection);
            lines.Add(">>>>>>>>>>: OBJECTS <<<<<<<<<<");
            lines.Add("");
            lines.Add("[[OBJECTS BEGIN]]");
            lines.Add("");
            foreach(var obj in map.objects)
            {
                lines.Add("[OBJECT BEGIN]");
                lines.Add($"obj_id: {obj.id}");
                lines.Add($"obj_tile_num: {obj.tileNum}");
                foreach(var prop in obj.properties)
                    lines.Add($"{prop.Key}: {prop.Value}");
                lines.Add("[OBJECT END]");
                lines.Add("");
            }
            lines.Add("");
            lines.Add("[[OBJECTS END]]");
            lines.Add("");
            File.WriteAllLines(file, lines);
        }

        static Map ParseMap(string file)
        {
            var lines = File.ReadAllLines(file);
            int curElev = -1;
            var mode = ParseSection.Pre;
            var map = new Map();
            MapObject curObject = null;
            foreach (var line in lines)
            {
                if (line == ">>>>>>>>>>: MAP_DATA <<<<<<<<<<")
                {
                    mode = ParseSection.Header;
                    continue;
                }

                if (line == ">>>>>>>>>>: MAP_SQUARES <<<<<<<<<<")
                {
                    mode = ParseSection.Squares;
                    continue;
                }

                if (line == ">>>>>>>>>>: SCRIPTS <<<<<<<<<<")
                {
                    mode = ParseSection.Scripts;
                    continue;
                }

                if (line == ">>>>>>>>>>: OBJECTS <<<<<<<<<<")
                {
                    mode = ParseSection.Objects;
                    continue;
                }


                if (mode == ParseSection.Pre)
                    continue;

                if (mode == ParseSection.Scripts)
                    map.scriptSection += line + "\n";

                if (line.Length == 0)
                    continue;

                if (mode == ParseSection.Header)
                {
                    var spl = line.Split(':');
                    map.headerData[spl[0]] = spl[1].Trim();
                }

                if (mode == ParseSection.Squares)
                {
                    var spl = line.Split(':');
                    if (spl[0] == "square_elev")
                        curElev = int.Parse(spl[1]);
                    else if (spl[0] == "sq")
                    {
                        var sqData = spl[1].Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        var sqId = int.Parse(sqData[0]);
                        map.squares.Add(new Square()
                        {
                            squareId = sqId,
                            elevation = curElev,
                            data = sqData[1] + " " + sqData[2]
                        });
                    }
                }

                if (mode == ParseSection.Objects)
                {
                    var spl = line.Split(':');
                    if (line == "[[OBJECTS BEGIN]]" || line == "[[OBJECTS END]]")
                        continue;

                    if (line == "[OBJECT BEGIN]")
                    {
                        curObject = new MapObject();
                        continue;
                    }

                    if (line == "[OBJECT END]")
                    {
                        map.objects.Add(curObject);
                        continue;
                    }

                    if (spl[0] == "obj_id")
                        curObject.id = int.Parse(spl[1]);
                    else if (spl[0] == "obj_tile_num")
                        curObject.tileNum = int.Parse(spl[1]);
                    else
                        curObject.properties[spl[0]] = spl[1].Trim();
                }

            }

            return map;
        }

        static void Main(string[] args)
        {
            //ParseConfig();
            if(args.Length < 5)
            {
                Console.WriteLine("mappet.exe <map a> <map b> <output map> -c \"<command>\"");
                Console.WriteLine("Example: MINE1.txt MINE3.txt flip.txt -c \"0: a2, 1:a1, 2: a0\"");
                Environment.Exit(-1);
            }
            var mapA = ParseMap(args[0]);
            var mapB = ParseMap(args[1]);

            var mapAName = mapA.headerData["map_name"].Trim();
            var mapBName = mapB.headerData["map_name"].Trim();
            List<ElevationOp> ops = new List<ElevationOp>();

            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                // <dest elevation>: <map><elev><map><elev>, <dest elevation> etc...
                // 0: a0, 1:b1, 2: a2
                // 0: a0b1b2 etc also possible
                if (a == "-c")
                {
                    var cmd = args[i + 1].Replace("\"", "");
                    var elevs = cmd.Split(',');
                    var elevOp = new ElevationOp();
                    foreach (var el in elevs)
                    {
                        var spl = el.Split(':');
                        var dst = int.Parse(spl[0]);
                        var srcMap = 0;
                        var srcElev = 0;

                        foreach (var c in spl[1].ToLower())
                        {
                            if (c == ' ')
                                continue;
                            if (c == 'a') srcMap = 0;
                            if (c == 'b') srcMap = 1;
                            if (char.IsDigit(c))
                            {
                                srcElev = int.Parse(c.ToString());
                                ops.Add(new ElevationOp()
                                {
                                    dstElevation = dst,
                                    map = srcMap,
                                    srcElevation = srcElev
                                });
                            }
                        }
                    }
                }
            }
            Console.WriteLine($"Map A = {mapAName}");
            Console.WriteLine($"Map B = {mapBName}");
            Console.WriteLine("----------------------");
            foreach (var op in ops)
            {
                var mapName = op.map == 0 ? mapAName : mapBName;
                Console.WriteLine($"Copy squares and objects from {mapName}, elevation {op.srcElevation} to elevation {op.dstElevation}");
            }
            Console.WriteLine("----------------------");
            var outputMap = new Map();
            outputMap.headerData = mapA.headerData;
            outputMap.scriptSection = mapA.scriptSection;

            foreach (var op in ops)
            {
                Map srcMap = op.map == 0 ? mapA : mapB;
                var srcMapName = srcMap.headerData["map_name"];
                foreach (var s in srcMap.squares.Where(x => x.elevation == op.srcElevation))
                {
                    var sq = new Square()
                    {
                        data = s.data,
                        elevation = op.dstElevation,
                        squareId = s.squareId
                    };
                    outputMap.squares.Add(sq);
                }

                foreach (var m in srcMap.objects.Where(x => int.Parse(x.properties["obj_elev"]) == op.srcElevation))
                {
                    var mapObject = new MapObject()
                    {
                        id = m.id,
                        tileNum = m.tileNum
                    };
                    foreach (var prop in m.properties)
                        mapObject.properties[prop.Key] = prop.Value;

                    mapObject.properties["obj_elev"] = op.dstElevation.ToString();
                    outputMap.objects.Add(mapObject);
                }
            }
            Console.WriteLine($"Saving output to {args[2]}");
            SaveMap(outputMap, args[2]);
            Console.WriteLine("Done! Press any key to end.");
            Console.ReadKey();
        }
    }
}
