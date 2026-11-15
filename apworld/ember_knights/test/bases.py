from test.bases import WorldTestBase

from .. import EmberKnightsWorld


class EmberKnightsTestBase(WorldTestBase):
    game = "Ember Knights"
    world: EmberKnightsWorld
