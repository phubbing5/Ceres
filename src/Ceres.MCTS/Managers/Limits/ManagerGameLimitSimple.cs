#region License notice

/*
  This file is part of the Ceres project at https://github.com/dje-dev/ceres.
  Copyright (C) 2020- by David Elliott and the Ceres Authors.

  Ceres is free software under the terms of the GNU General Public License v3.0.
  You should have received a copy of the GNU General Public License
  along with Ceres. If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

#region Using directives

using System;
using Ceres.Chess;

#endregion

namespace Ceres.MCTS.Managers.Limits
{
  /// <summary>
  /// Manager of time which estimates the optimal amount of time
  /// to spend on the next move using a very simplistic algorithm (1/20 of remaining search allowance).
  /// </summary>
  [Serializable]
  public class ManagerGameLimitSimple : IManagerGameLimit
  {
    public const float FRACTION_PER_MOVE = 1.0f / 20.0f;


    /// Determines how much time or nodes resource to
    /// allocate to the the current move in a game subject to
    /// a limit on total numbrer of time or nodes over 
    /// some number of moves (or possibly all moves).
    public ManagerGameLimitOutputs ComputeMoveAllocation(ManagerGameLimitInputs inputs)
    {
      if (inputs.MaxMovesToGo.HasValue && inputs.MaxMovesToGo < 2) 
        return new ManagerGameLimitOutputs(new SearchLimit(inputs.TargetLimitType,
                                                           inputs.RemainingFixedSelf * 0.99f));

      return (new ManagerGameLimitOutputs(new SearchLimit(inputs.TargetLimitType,
                                                     inputs.RemainingFixedSelf * FRACTION_PER_MOVE +
                                                     inputs.IncrementSelf)));
    }

  }
}