namespace LifxNet
{
    /// <summary>
    /// RGB Color structure
    /// </summary>
    public struct Color
    {
        /// <summary>
        /// Red
        /// </summary>
        public byte R { get; set; }

        /// <summary>
        /// Green
        /// </summary>
        public byte G { get; set; }

        /// <summary>
        /// Blue
        /// </summary>
        public byte B { get; set; }

        /// <summary>
        /// Returns a string representation of the color
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return $"R: {R}, G: {G}, B: {B}";
        }
    }
}