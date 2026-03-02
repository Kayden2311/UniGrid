package com.unigrid.DataAccess.entity;

import jakarta.persistence.*;
import lombok.Getter;
import lombok.Setter;
import org.hibernate.annotations.ColumnDefault;
import org.hibernate.annotations.Nationalized;

import java.time.Instant;

@Getter
@Setter
@Entity
@Table(name = "Member_Workspace", schema = "dbo")
public class MemberWorkspace {
    @EmbeddedId
    private MemberWorkspaceId id;

    @MapsId("memberId")
    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "memberId", nullable = false)
    private Member memberId;

    @MapsId("workspaceId")
    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "workspaceId", nullable = false)
    private Workspace workspaceId;

    @Nationalized
    @Column(name = "role", length = 20)
    private String role;

    @ColumnDefault("sysdatetime()")
    @Column(name = "joinAt")
    private Instant joinAt;


}