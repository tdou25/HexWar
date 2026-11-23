"""python plot.py runs/mine   -> regenerates runs/mine/training_curve.png from the saved CSVs."""
import sys
from hexwar.plotting import plot_from_csv
plot_from_csv(sys.argv[1])
