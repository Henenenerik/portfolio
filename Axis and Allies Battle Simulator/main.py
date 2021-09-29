import numpy as np
import networkx as nx
import copy
import plot
from scipy.stats import binom
from functools import reduce
from PyQt5.QtWidgets import QApplication
import sys
import GraphAnalysis as ga

'''
Written by: Henrik Johansson.
Main function and instructions found at bottom of file.
'''

def units_to_dice(units, is_attacker):
    res = [0, 0, 0, 0]
    if is_attacker:
        boosted_tact = min(units[4], units[3] + units[2])
        res[3] = units[5] + boosted_tact
        res[2] = units[3] + units[2] + units[4] - boosted_tact
        boosted_inf = min(units[1], units[0])
        res[1] = units[1] + boosted_inf
        res[0] = units[0] - boosted_inf
    else:
        res[3] = units[3]
        res[2] = units[4] + units[2]
        res[1] = units[1] + units[0]
        res[0] = units[5]
    return res


def calc_probs(units):
    res = np.zeros((1, np.sum(units) + 1))
    for x1 in range(units[0] + 1):
        for x2 in range(units[1] + 1):
            for x3 in range(units[2] + 1):
                for x4 in range(units[3] + 1):
                    res[0][x1+x2+x3+x4] += reduce(lambda a, b: a*b, binom.pmf([x1, x2, x3, x4], units, [1/6, 1/3, 1/2, 2/3]))
    return res


def take_casualties(units, n, is_attacker):
    reduced = copy.deepcopy(units)
    attacker_prio = [3, 4, 2, 1, 0, 5]
    defender_prio = [5, 4, 3, 2, 1, 0]

    for i in range(6):
        if is_attacker:
            x = min(n, units[defender_prio[i]])
            n -= x
            reduced[defender_prio[i]] -= x
        else:
            x = min(n, units[attacker_prio[i]])
            n -= x
            reduced[attacker_prio[i]] -= x
    return reduced


def perc(x, n=4):
    return "{:.2f}".format(round(x, n) * 100) + '%'


def run_sim(attacker, defender, plot_graph=False):
    #           I  A  T  F  T  S
    #           n  r  n  i  a  t
    #           f  t  k  g  c  r

    n_attacker = sum(attacker) + 1
    n_defender = sum(defender) + 1
    nn = n_attacker*n_defender

    # Construct combat graph
    G = nx.DiGraph()
    for i in range(n_attacker):
        for j in range(n_defender):
            G.add_node((n_attacker - i - 1, n_defender - j - 1))

    for i in range(n_attacker):
        for j in range(n_defender):
            att_ = take_casualties(attacker, i, True)
            def_ = take_casualties(defender, j, False)
            a_p = calc_probs(units_to_dice(att_, True))
            d_p = calc_probs(units_to_dice(def_, False))
            a_alive = n_attacker - i - 1
            d_alive = n_defender - j - 1
            for a_killed in range(len(a_p[0])):
                for d_killed in range(len(d_p[0])):
                    if a_killed <= d_alive and d_killed <= a_alive:
                        w = a_p[0][a_killed]*d_p[0][d_killed]
                        from_ = (a_alive, d_alive)
                        to_ = (a_alive-d_killed, d_alive-a_killed)
                        G.add_edge(from_, to_, weight=w)

    T = nx.adjacency_matrix(G)
    T = np.array(T.todense())

    # Remove edges which connect a node with itself and normalize rows.
    for i in range(len(T)):
        if T[i][i] != sum(T[i]):
            T[i][i] = 0
        T[i] = T[i]/sum(T[i])

    pi = np.zeros((1, len(T)))
    pi_last = copy.deepcopy(pi)
    pi[0][0] = 1

    # Propagate combat results
    while np.linalg.norm(pi-pi_last) != 0:
        pi_last = copy.deepcopy(pi)
        pi = np.matmul(pi, T)

    # Validate probability
    if abs(sum(pi[0]) - 1) > 1e-10:
        print("Panic in the distribution array: sum(pi) = " + str(sum(pi[0])))
        return

    # Extract probabilities
    pi = pi[0]
    a_c = n_attacker - 1
    le = len(pi)
    res = np.zeros(n_attacker + n_defender - 1)
    count = 0
    count_2 = 1
    mid_point = n_attacker - 1
    for i in range(le):
        if pi[i] != 0:
            if a_c > 0:
                res[count] = pi[i]
                count += 1
                a_c -= 1
            elif i == le - 1:
                res[mid_point] = pi[i]
            else:
                res[-count_2] = pi[i]
                count_2 += 1


    print("Probability of successful attack: " + perc(sum(res[:mid_point])))
    print("Probabilities of outcomes: ")
    for i in range(len(res)):
        if (i < mid_point):
            print("Attacker survives with " + str((mid_point - i)) + " units with probability: " + perc(res[i]))
        elif (i == mid_point):
            print("No survivors with probability: " + perc(res[i]))
        else:
            print("Defender survives with " + str((i - mid_point)) + " units with probability: " + perc(res[i]))

    if plot_graph:
        app = QApplication([sys.argv])
        main_plot = plot.MainWindow(res)
        main_plot.show()
        app.exec_()

    return res, mid_point


def multi_sim(iterations):
    prob = []
    n_units = iterations
    for i in range(n_units):
        attacker = [i + 1, i + 1, 0, 0, 0, 0]
        defender = [2 * i + 2, 0, 0, 0, 0, 0]
        res, mid_point = run_sim(attacker, defender, True)
        prob.append(sum(res[:mid_point]))
    print(str([i + 1 for i in range(n_units)]))
    print(str(np.round(prob, 3)))


def main():
    '''
    Pass two 6 element vectors to run_sim which represents attackers and defenders.
    Each element corresponds to a unit type as shown by the legend.
    run_sim boolean determines whether to plot the results or only show print output.
    The plot is a bar graph where each bar represents the probability of an outcome.
    The lowest x-value on the bar graph indicates when all attackers survive, the highest x-value when all defenders survive.

    Legend
    Infantry | Artillery | Tanks | Fighter Aircraft | Tactical Bombers | Strategic Bombers
    '''
    attackers = [0, 0, 3, 0, 0, 0]
    defenders = [2, 2, 0, 0, 0, 0]

    run_sim(attackers, defenders, True)


main()
