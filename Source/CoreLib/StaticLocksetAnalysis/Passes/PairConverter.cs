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
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using Microsoft.Boogie;
using Microsoft.Basetypes;

using Whoop.Regions;

namespace Whoop.SLA
{
  internal class PairConverter : IPairConverter
  {
    private AnalysisContext AC;
    private string FunctionName;

    public PairConverter(AnalysisContext ac, string functionName)
    {
      Contract.Requires(ac != null && functionName != null);
      this.AC = ac;
      this.FunctionName = functionName;
    }

    public void Run()
    {
      this.ConvertAsyncFuncs();

      foreach (var region in this.AC.LocksetAnalysisRegions)
        this.SplitCallsInRegion(region);

      this.RemoveOriginalAsyncFuncs();
    }

    private void ConvertAsyncFuncs()
    {
      foreach (var ep in PairConverterUtil.FunctionPairs[this.FunctionName])
      {
        Implementation impl = this.AC.GetImplementation(ep.Item1);
        List<Implementation> implList = new List<Implementation>();

        foreach (var v in ep.Item2) implList.Add(this.AC.GetImplementation(v));

        this.CreateNewAsyncFuncPair(impl, implList);

        Constant cons = this.AC.GetConstant(ep.Item1);
        List<Constant> consList = new List<Constant>();

        foreach (var v in ep.Item2) consList.Add(this.AC.GetConstant(v));

        this.CreateNewConstant(cons, consList);
      }
    }

    private void CreateNewAsyncFuncPair(Implementation impl, List<Implementation> implList)
    {
      LocksetAnalysisRegion region = new LocksetAnalysisRegion(this.AC, impl, implList);
      this.AC.Program.TopLevelDeclarations.Add(region.Procedure());
      this.AC.Program.TopLevelDeclarations.Add(region.Implementation());
      this.AC.ResContext.AddProcedure(region.Procedure());
      this.AC.LocksetAnalysisRegions.Add(region);
    }

    private void SplitCallsInRegion(LocksetAnalysisRegion region)
    {
      foreach (var c in region.Logger().Cmds())
      {
        if (!(c is CallCmd)) continue;
        string callee = (c as CallCmd).callee;

        if (this.AC.GetImplementation(callee + "$log") != null ||
          this.AC.GetImplementation(callee + "$check") != null)
        {
          (c as CallCmd).callee = callee + "$log";
        }
      }

      foreach (var checker in region.Checkers())
      {
        foreach (var c in checker.Cmds())
        {
          if (!(c is CallCmd)) continue;
          string callee = (c as CallCmd).callee;

          if (this.AC.GetImplementation(callee + "$log") != null ||
            this.AC.GetImplementation(callee + "$check") != null)
          {
            (c as CallCmd).callee = callee + "$check";
          }
        }
      }
    }

    private void CreateNewConstant(Constant cons, List<Constant> consList)
    {
      string consName = "$";

      if (PairConverterUtil.FunctionPairingMethod != FunctionPairingMethod.QUADRATIC)
        consName += cons.Name;
      else
        consName += cons.Name + "$" + consList[0].Name;

      Constant newCons = new Constant(Token.NoToken,
        new TypedIdent(Token.NoToken, consName,
          this.AC.MemoryModelType), true);

      this.AC.Program.TopLevelDeclarations.Add(newCons);
    }

    private void RemoveOriginalAsyncFuncs()
    {
      foreach (var kvp in PairConverterUtil.FunctionPairs)
      {
        foreach (var ep in kvp.Value)
        {
          if (!this.AC.Program.TopLevelDeclarations.OfType<Implementation>().
            ToList().Any(val => val.Name.Equals(ep.Item1)))
            continue;
          if (ep.Item1 == this.AC.InitFunc.Name)
            continue;

          this.AC.Program.TopLevelDeclarations.Remove(this.AC.GetConstant(ep.Item1));
          this.AC.Program.TopLevelDeclarations.Remove(this.AC.GetImplementation(ep.Item1).Proc);
          this.AC.Program.TopLevelDeclarations.Remove(this.AC.GetImplementation(ep.Item1));
        }
      }
    }
  }
}
