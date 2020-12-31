#region License notice

/*
  This file is part of the Ceres project at https://github.com/dje-dev/ceres.
  Copyright (C) 2020- by David Elliott and the Ceres Authors.

  Ceres is free software under the terms of the GNU General Public License v3.0.
  You should have received a copy of the GNU General Public License
  along with Ceres. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

#region Using directive

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;
using Ceres.Base.DataTypes;
using Ceres.Chess.UserSettings;
using Ceres.Features.Players;
using Ceres.Features.Suites;

#endregion

namespace Ceres.Commands
{
  public record FeatureSuiteParams : FeaturePlayWithOpponent
  {
    public string EPD { init; get; }

    public static FeatureSuiteParams ParseSuiteCommand(string fen, string args)
    {
      if (fen != null) throw new Exception("SUITE command expected to be followed by sequence of key=value pairs");

      List<string> validArgs = new List<string>(FEATURE_PLAY_COMMON_ARGS);
      validArgs.Add("EPD");

      KeyValueSetParsed keys = new KeyValueSetParsed(args, validArgs);

      FeatureSuiteParams parms =  new FeatureSuiteParams()
      {
        EPD = keys.GetValue("EPD"),
      };

      // Add in all the fields from the base class
      parms.ParseBaseFields(args);

      return parms;
    }


    public static void RunSuiteTest(FeatureSuiteParams suiteParams)
    {
      (EnginePlayerDef player, EnginePlayerDef playerExternal) = suiteParams.GetEngineDefs(false);

      // Add .EPD if not already present
      string suiteName = suiteParams.EPD;
      if (!suiteName.ToUpper().EndsWith(".EPD")) suiteName += ".epd";

      string epdFilename = Path.Combine(CeresUserSettingsManager.Settings.DirEPD, suiteName);
      if (!File.Exists(epdFilename)) throw new Exception($"Specified EPD not found: {epdFilename}");

      SuiteTestDef suiteDef = new SuiteTestDef($"SUITE TEST {CeresUserSettingsManager.Settings.DirEPD}", 
                                               epdFilename, player, null, playerExternal);

      SuiteTestRunner suiteTest = new SuiteTestRunner(suiteDef);
      suiteTest.Run(1, true, false);
    }
  }
}
