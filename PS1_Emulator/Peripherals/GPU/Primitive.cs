namespace PSXEmulator {
    public interface Primitive {
        public void add(uint value);
        public void draw(ref Renderer window);

        public bool isReady();

    }
}
