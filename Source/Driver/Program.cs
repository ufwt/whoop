﻿// ===-----------------------------------------------------------------------==//
//
//                 Whoop - a Verifier for Device Drivers
//
//  Copyright (c) 2013-2014 Pantazis Deligiannis (p.deligiannis@imperial.ac.uk)
//
//  This file is distributed under the Microsoft Public License.  See
//  LICENSE.TXT for details.
//
// ===----------------------------------------------------------------------===//

using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics.Contracts;

using Microsoft.Boogie;
using Whoop.Domain.Drivers;

namespace Whoop.Driver
{
  using FunctionPairType = Tuple<string, List<Tuple<string, List<string>>>, AnalysisContext>;

  public class Program
  {
    public static void Main(string[] args)
    {
      Contract.Requires(cce.NonNullElements(args));

      CommandLineOptions.Install(new DriverCommandLineOptions());

      try
      {
        DriverCommandLineOptions.Get().RunningBoogieFromCommandLine = true;

        if (!DriverCommandLineOptions.Get().Parse(args))
        {
          Environment.Exit((int)Outcome.FatalError);
        }

        if (DriverCommandLineOptions.Get().Files.Count == 0)
        {
          Whoop.IO.Reporter.ErrorWriteLine("Whoop: error: no input files were specified");
          Environment.Exit((int)Outcome.FatalError);
        }

        List<string> fileList = new List<string>();

        foreach (string file in DriverCommandLineOptions.Get().Files)
        {
          string extension = Path.GetExtension(file);
          if (extension != null)
          {
            extension = extension.ToLower();
          }
          fileList.Add(file);
        }

        foreach (string file in fileList)
        {
          Contract.Assert(file != null);
          string extension = Path.GetExtension(file);
          if (extension != null)
          {
            extension = extension.ToLower();
          }
          if (extension != ".bpl")
          {
            Whoop.IO.Reporter.ErrorWriteLine("Whoop: error: {0} is not a .bpl file", file);
            Environment.Exit((int)Outcome.FatalError);
          }
        }

        DeviceDriver.ParseAndInitialize(fileList);

        PipelineStatistics stats = new PipelineStatistics();
        WhoopErrorReporter errorReporter = new WhoopErrorReporter();
        Outcome outcome = Outcome.Done;

        foreach (var pair in DeviceDriver.EntryPointPairs)
        {
          AnalysisContext ac = new AnalysisContextParser(fileList[fileList.Count - 1],
            "wbpl").ParseNew(new List<string>
            {
              "check_" + pair.Item1.Name + "_" + pair.Item2.Name,
              pair.Item1.Name + "_instrumented",
              pair.Item2.Name + "_instrumented"
            });

          Outcome oc = new StaticLocksetAnalyser(ac, pair.Item1, pair.Item2, stats, errorReporter).Run();

          if (oc != Outcome.LocksetAnalysisError)
          {
            outcome = oc;
          }
        }

        Environment.Exit((int)outcome);
      }
      catch (Exception e)
      {
        Console.Error.Write("Exception thrown in Whoop: ");
        Console.Error.WriteLine(e);
        Environment.Exit((int)Outcome.FatalError);
      }
    }
  }
}
