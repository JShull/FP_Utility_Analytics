namespace FuzzPhyte.Utility.Analytics
{
    using UnityEngine;

    public static class FP_WeightedPicker
    {
        /// <summary>
        /// Pick a bin index from remaining item counts.
        /// - Bins with 0 items are strictly unpickable (0% chance).
        /// - Optional smoothing adds to positive counts only (zeros remain zero).
        /// - Optional temperature scales the distribution: p_i ∝ (p_i)^temperature.
        /// Returns -1 if no bins are pickable.
        /// </summary>
        /// <param name="counts">Array length ≥ 1, each ≥ 0.</param>
        /// <param name="smoothK">Add-k smoothing applied ONLY to bins with count > 0. Use 0 for none.</param>
        /// <param name="temperature">>1 sharpens; (0,1) flattens; 1 means no change.</param>
        public static int PickFromCounts(int[] counts, float smoothK = 0f, float temperature = 1f)
        {
            if (counts == null || counts.Length == 0)
                return -1;

            var weights = new float[counts.Length];
            for (int i = 0; i < counts.Length; i++)
            {
                int c = counts[i] < 0 ? 0 : counts[i];
                // Strict zero policy: if c == 0, weight is 0, regardless of smoothing.
                weights[i] = (c > 0) ? (c + smoothK) : 0f;
            }

            return SelectIndex(weights, temperature);
        }

        /// <summary>
        /// Pick a bin index from probabilities (not required to sum to 1).
        /// - Non-positive probs are treated as 0 and are unpickable.
        /// - Optional temperature scales the distribution: p_i ∝ (p_i)^temperature.
        /// Returns -1 if all bins are non-positive.
        /// </summary>
        /// <param name="probs">Array length ≥ 1, each ≥ 0 recommended.</param>
        /// <param name="temperature">>1 sharpens; (0,1) flattens; 1 means no change.</param>
        public static int PickFromProbabilities(float[] probs, float temperature = 1f)
        {
            if (probs == null || probs.Length == 0)
                return -1;

            var weights = new float[probs.Length];
            for (int i = 0; i < probs.Length; i++)
            {
                float p = probs[i];
                weights[i] = (p > 0f) ? p : 0f; // strict zero for non-positive
            }

            return SelectIndex(weights, temperature);
        }

        /// <summary>
        /// Core selection with temperature scaling and strict zero handling.
        /// Expects non-negative weights. Returns -1 if all weights are zero.
        /// </summary>
        private static int SelectIndex(float[] weights, float temperature)
        {
            // Normalize weights to probabilities (sum > 0)
            float sum = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] < 0f) weights[i] = 0f; // clamp negatives
                sum += weights[i];
            }

            if (sum <= 0f)
                return -1; // nothing pickable

            // Convert to probs
            for (int i = 0; i < weights.Length; i++)
                weights[i] = weights[i] / sum;

            // Temperature scaling
            if (!Mathf.Approximately(temperature, 1f))
            {
                float renorm = 0f;
                for (int i = 0; i < weights.Length; i++)
                {
                    if (weights[i] > 0f)
                        weights[i] = Mathf.Pow(weights[i], temperature);
                    // zero stays zero
                    renorm += weights[i];
                }

                if (renorm <= 0f)
                    return -1; // all went to zero via extreme temperature

                for (int i = 0; i < weights.Length; i++)
                    weights[i] /= renorm;
            }

            // Roulette-wheel selection (zeros are naturally unpickable)
            float r = Random.value; // [0,1)
            float cumulative = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                cumulative += weights[i];
                if (r <= cumulative)
                    return i;
            }

            // Floating-point safeguard
            return weights.Length - 1;
        }
    }
}
