using System;

namespace PSXSharp {
    public class Matrix3 {
        public Vector3[] vectors; 
        public Matrix3() {
            vectors = new Vector3[3];
            for (int i = 0; i < 3; i++) {
                vectors[i] = new Vector3();
            }
        }
        public Matrix3(Vector3[] vectors) {
            this.vectors = vectors;
        }
        public short GetElement(int R, int C) {
            return vectors[C - 1].GetElement(R);
        }
        public void SetElement(int R, int C, short value) {
            vectors[C - 1].SetElement(R, value);
        }
        public void PrintMatrix() {
            for (int i = 1; i < 4; i++) {
                for (int j = 1; j < 4; j++) {
                    Console.Write(GetElement(i,j).ToString("x").PadLeft(4,'0') + "  ");
                }
                Console.WriteLine("\n");
            }
        }
    }
}
