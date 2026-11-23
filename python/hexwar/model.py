import torch
import torch.nn as nn
from torch.distributions import Categorical

from .env import OBS_SIZE, N_ACTIONS


class ActorCritic(nn.Module):
    """MLP over the flat 1635-float observation with a masked categorical policy over the 99 actions."""

    def __init__(self, hidden=512, obs_dim=OBS_SIZE, n_actions=N_ACTIONS):
        super().__init__()
        self.hidden = hidden
        self.body = nn.Sequential(
            nn.Linear(obs_dim, hidden), nn.ReLU(),
            nn.Linear(hidden, hidden), nn.ReLU(),
            nn.Linear(hidden, hidden), nn.ReLU())
        self.pi = nn.Linear(hidden, n_actions)
        self.v = nn.Linear(hidden, 1)
        for m in self.body:
            if isinstance(m, nn.Linear):
                nn.init.orthogonal_(m.weight, 2 ** 0.5); nn.init.zeros_(m.bias)
        nn.init.orthogonal_(self.pi.weight, 0.01); nn.init.zeros_(self.pi.bias)
        nn.init.orthogonal_(self.v.weight, 1.0); nn.init.zeros_(self.v.bias)

    def forward(self, obs, mask):
        h = self.body(obs)
        logits = self.pi(h).masked_fill(~mask, -1e9)
        return Categorical(logits=logits), self.v(h).squeeze(-1)

    @torch.no_grad()
    def act(self, obs, mask, greedy=False):
        dist, value = self.forward(obs, mask)
        a = dist.probs.argmax(-1) if greedy else dist.sample()
        return a, dist.log_prob(a), value
