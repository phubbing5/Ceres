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
using System.Threading.Tasks;

using Ceres.Base;
using Ceres.Base.DataTypes;
using Ceres.Base.Threading;
using Ceres.Chess.EncodedPositions;
using Ceres.Chess.NetEvaluation.Batch;
using Chess.Ceres.NNEvaluators;
using Ceres.Chess.NNEvaluators.Internals;
using System.Runtime.CompilerServices;
using Ceres.Chess.NNFiles;
using static Ceres.Chess.NNEvaluators.LC0DLL.LCO_Interop;
using Ceres.Chess.LC0.Batches;
using Ceres.Base.Benchmarking;

#endregion

namespace Ceres.Chess.NNEvaluators
{
  /// <summary>
  /// Interface to the neural network (and tablebase) backend 
  /// logic from Leela Chess Zero (LC0), accessed via a 
  /// custom compiled library with a small patch to expose required functionality.
  /// </summary>
  public class NNEvaluatorLC0 : NNEvaluator
  {
    public override bool IsWDL => isWDL;
    public override bool HasM => hasM;

    readonly bool isWDL;
    readonly bool hasM;

    /// <summary>
    /// Types of input(s) required by the evaluator.
    /// </summary>
    public override InputTypes InputsRequired => InputTypes.Boards | InputTypes.Moves;

    CompressedPolicyVector[] policies;
    FP16[] l;
    FP16[] w;
    FP16[] m;

    internal readonly LC0LibraryNNEvaluator Evaluator;

    // TODO: Add a Dispose/Release which calls Dispose on associated LC0DllNNEvaluator 

    public NNEvaluatorLC0(INNWeightsFileInfo net, int[] gpuIDs, NNEvaluatorPrecision precision = NNEvaluatorPrecision.FP16)
    {
      if (gpuIDs.Length != 1) throw new ArgumentException(nameof(gpuIDs), "Implementation limitation: one GPU id must be specified");
      if (precision != NNEvaluatorPrecision.FP16) throw new ArgumentException(nameof(precision), "Implementation: only FP16 supported");

      isWDL = net.IsWDL;
      hasM = net.HasMovesLeft;

      policies = new CompressedPolicyVector[MAX_BATCH_SIZE];
      w = new FP16[MAX_BATCH_SIZE];
      l = isWDL ? new FP16[MAX_BATCH_SIZE] : null;
      m = isWDL ? new FP16[MAX_BATCH_SIZE] : null;

      // Create NN evaluator and attach to it
      //TODO: set precision
      //LC0Engine engine = LaunchLC0Server.LaunchProcess(net.LC0WeightsFilename, gpuIDs, precision);
      Evaluator = new LC0LibraryNNEvaluator(net.FileName, gpuIDs[0]);
    }


    /// <summary>
    /// Constructor when only one GPU is requested.
    /// </summary>
    /// <param name="net"></param>
    /// <param name="gpuID"></param>
    /// <param name="precision"></param>
    public NNEvaluatorLC0(INNWeightsFileInfo net, int gpuID = 0, NNEvaluatorPrecision precision = NNEvaluatorPrecision.FP16)
      : this(net, new int[] { gpuID }, precision)
    {
    }


    /// <summary>
    /// If this evaluator produces the same output as another specified evaluator.
    /// </summary>
    /// <param name="evaluator"></param>
    /// <returns></returns>
    public override bool IsEquivalentTo(NNEvaluator evaluator)
    {
      return EngineNetworkID == evaluator.EngineNetworkID;
    }

        
    public override IPositionEvaluationBatch EvaluateIntoBuffers(IEncodedPositionBatchFlat positions, bool retrieveSupplementalResults = false)
    {
      if (positions.Moves == null) throw new Exception("NNEvaluatorLC0NNEvaluator requires Moves to be provided");
      if (retrieveSupplementalResults) throw new NotImplementedException("retrieveSupplementalResults not supported");

      Evaluator.EvaluateNN(positions, positions.Positions);

      const int NUM_POSITIONS_PER_THREAD = 40;
      ParallelOptions parallelOptions = ParallelUtils.ParallelOptions(positions.NumPos, NUM_POSITIONS_PER_THREAD);
      Parallel.For(0, positions.NumPos, parallelOptions, PreparePosition);

      return new PositionEvaluationBatch(IsWDL, HasM, positions.NumPos, policies, w, l, m, null, new TimingStats()); ;
    }


    [SkipLocalsInit]
    private void PreparePosition(int i)
    {
      ref readonly CeresTransferBlockOutItem thisItemsOut = ref Evaluator.ItemsOut[i];
      if (IsWDL)
      {
        float thisQ = thisItemsOut.Q;
        float thisD = thisItemsOut.D;
        float thisM = thisItemsOut.M;
        float thisW = (1.0f - thisD + thisQ) / 2.0f;
        float thisL = 1.0f - (thisD + thisW);

        w[i] = (FP16)thisW;
        l[i] = (FP16)thisL;
        m[i] = (FP16)thisM;
      }
      else
        w[i] = (FP16)thisItemsOut.Q;

      int numMoves = Evaluator.ItemsIn[i].NumMoves;

      if (numMoves > 0)
      {
        int numMovesToSave = Math.Min(CompressedPolicyVector.NUM_MOVE_SLOTS, numMoves);

        // We need to sort here to make sure the highest P are in the first NUM_MOVE_SLOTS
        Span<PolicyVectorCompressedInitializerFromProbs.ProbEntry> probs = stackalloc PolicyVectorCompressedInitializerFromProbs.ProbEntry[numMoves];
        ref readonly CeresTransferBlockInItem refItem = ref Evaluator.ItemsIn[i];
        unsafe
        {
          for (int j = 0; j < numMoves; j++)
            probs[j] = new PolicyVectorCompressedInitializerFromProbs.ProbEntry(refItem.Moves[j], thisItemsOut.P[j]);
        }

        PolicyVectorCompressedInitializerFromProbs.InitializeFromProbsArray(ref policies[i], numMoves, numMovesToSave, probs);
      }
    }

    protected override void DoShutdown()
    {
      Evaluator.Dispose();
    }


    public override string ToString()
    {
      return $"<NNEvaluatorLC0Dll {Evaluator}>";
    }

  }


}

