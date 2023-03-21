using System.Text;
using System.Xml;

namespace OuDiaConverter;

internal enum Houkou {
  Nobori,
  Kudari,
}

internal class Time {
  public int Hour { get; }
  public int Minute { get; }
  public Time(int hour, int minute) {
    Hour = hour;
    Minute = minute;
  }
}

internal class Program
{
  static void Main()
  {
    var audPath = "D:\\Users\\kamo\\Desktop\\SchedulerTest.oud2";
    var configPath = "D:\\SteamLibrary\\steamapps\\common\\Cities_Skylines\\TimeTables_Template.xml";

    var ekiJikokus = GetEkiJikokus(audPath);
    UpdateTrainSchedulerConfig(ekiJikokus, configPath, "118");
  }

  private static Dictionary<(string, string), List<Time>> GetEkiJikokus(string audPath) {
    System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    var lines = File.ReadAllLines(audPath, Encoding.GetEncoding("Shift_JIS"));

    var dias = GetDias(lines);
    if (dias.Count != 1) {
      throw new Exception("ダイヤ数が1ではありません");
    }

    var ekis = GetEkis(lines);
    var ressyas =
      GetRessyas(lines)
      .Select(x => {
        var (ressha, houkou) = x;
        return (
          ressha.Split(',')
          .Select(jikoku => {
            if (jikoku == "") {
              return null;
            }
            return ParseEkiJikoku(jikoku);
          }),
          houkou
        );
      });
    // get jikokus of each eki
    var ekiJikokus = new Dictionary<(string, string), List<Time>>();
    for(var ekiIndex = 0; ekiIndex < ekis.Count - 1; ekiIndex++) {
      ekiJikokus[(ekis[ekiIndex], ekis[ekiIndex + 1])] = new List<Time>();
      ekiJikokus[(ekis[ekis.Count - ekiIndex - 1], ekis[ekis.Count - ekiIndex - 2])] = new List<Time>();
    }
    foreach (var (ressya, houkou) in ressyas) {
      var ekiIndex = 0;
      foreach (var jikoku in ressya) {
        if (jikoku != null) {
          var adjustedEkiIndex = houkou == Houkou.Kudari ? ekiIndex : ekis.Count - 1 - ekiIndex;
          var currentEki = ekis[adjustedEkiIndex];
          var nextEki = ekis[adjustedEkiIndex + (houkou == Houkou.Kudari ? 1 : -1)];
          ekiJikokus[(currentEki, nextEki)].Add(jikoku);
        }
        ekiIndex++;
      }
    }

    return ekiJikokus;
  }

  private static void UpdateTrainSchedulerConfig(Dictionary<(string, string), List<Time>> ekiJikokus, string configPath, string lineId)
  {
    var config = File.ReadAllText(configPath, Encoding.GetEncoding("UTF-8"));
    var doc = new XmlDocument();
    doc.LoadXml(config);
    var root = doc.DocumentElement;
    var lines = root.SelectNodes("Lines/Line");
    foreach (XmlNode line in lines) {
      if (line.Attributes["LineID"].Value != lineId) {
        continue;
      }
      var stops = line.SelectNodes("Stops/Stop");
      foreach (XmlNode stop in stops) {
        var currentStation = stop.Attributes["Name"].Value;
        var nextStation = stop.Attributes["NextName"].Value;
        
        if (ekiJikokus.ContainsKey((currentStation, nextStation))) {
          var jikokus = ekiJikokus[(currentStation, nextStation)];
          var departures = stop.SelectNodes("Departures/Departure");
          foreach (XmlNode departure in departures) {
            departure.ParentNode.RemoveChild(departure);
          }
          // add jikokus to Departures
          foreach (var jikoku in jikokus) {
            var departure = doc.CreateElement("Departure");
            departure.InnerText = $"{jikoku.Hour:00}{jikoku.Minute:00}";
            stop.SelectSingleNode("Departures").AppendChild(departure);
          }
        } else {
          Console.WriteLine($"No jikokus found for {currentStation} to {nextStation}");
        }
      }
    }
    doc.Save(configPath);
  }

  private static List<(string, int)> GetDias(string[] lines) {
    var dias = new List<(string, int)>();
    for (int i = 0; i < lines.Length; i++) {
      var line = lines[i];
      if (line == "Dia.") {
        var dia = GetProperty(lines, i, "DiaName");
        dias.Add((dia, i));
      }
    }
    return dias;
  }

  private static Time ParseEkiJikoku(string jikoku) {
    var jikokus = jikoku.Split(';')[1].Split('$')[0].Split('/').Select(j => ParseJikoku(j)).ToList();
    if (jikokus.Count == 1) {
      return jikokus[0];
    } else {
      return jikokus[1];
    }
  }

  private static Time ParseJikoku(string text) {
    if (text == "") {
      return null;
    }
    if (text.Length == 3) {
      text = "0" + text;
    }
    var hour = int.Parse(text.Substring(0, 2));
    var minute = int.Parse(text.Substring(2, 2));
    return new Time(hour, minute);
  }

  private static int GetLastIndexOfBlock(string[] lines, int startIndex) {
    for (int i = startIndex + 1; i < lines.Length; i++) {
      var line = lines[i];
      if (line.Last() == '.') {
        return i;
      }
    }
    return lines.Length;
  }

  private static List<string> GetEkis(string[] lines) {
    var ekis = new List<string>();
    for (int i = 0; i < lines.Length; i++) {
      var line = lines[i];
      if (line == "Eki.") {
        var eki = GetProperty(lines, i, "Ekimei");
        ekis.Add(eki);
      }
    }
    return ekis;
  }

  private static List<(string, Houkou)> GetRessyas(string[] lines) {
    var ressyas = new List<(string, Houkou)>();
    for (int i = 0; i < lines.Length; i++) {
      var line = lines[i];
      if (line == "Ressya.") {
        var ressya = GetProperty(lines, i, "EkiJikoku");
        var houkou = GetProperty(lines, i, "Houkou") == "Nobori" ? Houkou.Nobori : Houkou.Kudari;
        
        ressyas.Add((ressya, houkou));
      }
    }
    return ressyas;
  }

  private static string GetProperty(string[] lines, int i, string propertyName) {
    var lastIndexOfBlock = GetLastIndexOfBlock(lines, i);
    for (int j = i + 1; j < lastIndexOfBlock; j++) {
      var line = lines[j];
      if (line.StartsWith(propertyName + "=")) {
        return line.Substring(propertyName.Length + 1);
      }
    }
    throw new Exception("Property not found (" + propertyName + ")");
  }
}
