namespace PSXSharp {
    public interface Primitive {
        public void Add(uint value);
        public void Draw(ref Renderer window);
        public bool IsReady();
    }
}
